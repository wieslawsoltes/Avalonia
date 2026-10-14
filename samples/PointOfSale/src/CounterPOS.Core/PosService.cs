namespace CounterPOS.Core;
/// <summary>Serializes commands per session; persistence CAS protects concurrent tabs. Every operation edits a detached snapshot and publishes only after durable acceptance. Local role rules are not trusted authentication.</summary>
public sealed class PosService(IPosStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _revision = -1;
    public static Operator DemoManager { get; } = new("manager-demo", "Alex Morgan", OperatorRole.Manager);
    public static Operator DemoCashier { get; } = new("cashier-demo", "Jamie Lee", OperatorRole.Cashier);
    public Operator Actor { get; set; } = DemoManager;
    public async Task<PosState> InitializeAsync(PosState? seed = null, CancellationToken token = default)
    {
        var current = await store.ReadAsync(token);
        if (current is null)
        {
            var initial = seed ?? DemoData.Create();
            if (initial.Revision != 0) throw new PosException("Initial revision must be zero.");
            await store.CompareExchangeAsync(-1, initial, token);
        }
        return await LoadAsync(token);
    }
    public async Task<PosState> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var state = await store.ReadAsync(token) ?? throw new PosException("Register has not been initialized.");
            _revision = state.Revision;
            return StateJson.Clone(state);
        }
        finally { _gate.Release(); }
    }
    private async Task<T> ChangeAsync<T>(string action, string detail, Func<PosState, Operator, T> change, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            var state = await store.ReadAsync(token) ?? throw new PosException("Register has not been initialized.");
            if (state.Revision != _revision) throw new ConcurrencyException();
            var actor = Actor;
            var result = change(state, actor);
            Pricing.Calculate(state.ActiveCart, state.Currency);
            state.Revision = checked(state.Revision + 1);
            state.Audit.Add(new AuditEntry(state.Revision, DateTimeOffset.UtcNow, actor.Id, action, detail));
            if (!await store.CompareExchangeAsync(_revision, state, token)) throw new ConcurrencyException();
            _revision = state.Revision;
            return result;
        }
        finally { _gate.Release(); }
    }
    private Task ChangeAsync(string action, string detail, Action<PosState, Operator> change) => ChangeAsync(action, detail, (s, a) => { change(s, a); return true; });
    private static void Manager(Operator actor)
    {
        if (actor.Role != OperatorRole.Manager) throw new PosException("Manager permission is required for this action.");
    }
    private static string Reason(string reason)
    {
        reason = reason.Trim();
        if (reason.Length is < 3 or > 250) throw new PosException("Enter a reason between 3 and 250 characters.");
        return reason;
    }
    private static Product FindProduct(PosState state, string id) => state.Products.FirstOrDefault(p => p.Id == id) ?? throw new PosException("Product no longer exists.");
    private static int FindLine(PosState state, string id)
    {
        var index = state.ActiveCart.Lines.FindIndex(x => x.Id == id);
        return index >= 0 ? index : throw new PosException("The cart line no longer exists.");
    }
    public Task AddProductAsync(string productId, decimal? quantity = null) => ChangeAsync("cart.add", productId, (s, _) =>
    {
        var p = FindProduct(s, productId);
        if (!p.Active) throw new PosException("Product is not available for sale.");
        var qty = quantity ?? (p.Weighted ? .25m : 1m);
        Pricing.Quantity(qty, p.Weighted);
        var existing = s.ActiveCart.Lines.FindIndex(x => x.ProductId == productId && x.UnitPrice == p.Price && x.DiscountPercent == 0);
        var requested = qty + s.ActiveCart.Lines.Where(x => x.ProductId == productId).Sum(x => x.Quantity);
        if (requested > p.Stock) throw new PosException($"Only {p.Stock:0.###} units of {p.Name} are in stock.");
        if (existing >= 0)
        {
            var line = s.ActiveCart.Lines[existing];
            s.ActiveCart.Lines[existing] = line with { Quantity = line.Quantity + qty };
        }
        else s.ActiveCart.Lines.Add(new CartLine(Guid.NewGuid().ToString("N"), p.Id, p.Sku, p.Name, p.Price, p.TaxRate, p.TaxMode, p.Weighted, qty));
    });
    public Task SetQuantityAsync(string lineId, decimal quantity) => ChangeAsync("cart.quantity", lineId, (s, _) =>
    {
        var index = FindLine(s, lineId);
        var line = s.ActiveCart.Lines[index];
        Pricing.Quantity(quantity, line.Weighted);
        var total = s.ActiveCart.Lines.Where(x => x.ProductId == line.ProductId && x.Id != lineId).Sum(x => x.Quantity) + quantity;
        if (total > FindProduct(s, line.ProductId).Stock) throw new PosException("Requested quantity exceeds available stock.");
        s.ActiveCart.Lines[index] = line with { Quantity = quantity };
    });
    public Task RemoveLineAsync(string lineId) => ChangeAsync("cart.remove", lineId, (s, _) =>
    {
        s.ActiveCart.Lines.RemoveAt(FindLine(s, lineId));
        if (s.ActiveCart.Lines.Count == 0) { s.ActiveCart.DiscountAmount = 0; s.ActiveCart.DiscountPercent = 0; }
        else s.ActiveCart.DiscountAmount = 0;
    });
    public Task DiscountAsync(decimal percent, decimal amount = 0) => ChangeAsync("cart.discount", $"{percent}% + {amount}", (s, a) =>
    {
        Pricing.Percent(percent); Pricing.Amount(amount, s.Currency);
        if (percent > 10 || amount != 0) Manager(a);
        s.ActiveCart.DiscountPercent = percent; s.ActiveCart.DiscountAmount = amount;
    });
    public Task LineDiscountAsync(string lineId, decimal percent) => ChangeAsync("cart.line-discount", lineId, (s, a) =>
    {
        Pricing.Percent(percent); if (percent > 10) Manager(a);
        var index = FindLine(s, lineId);
        s.ActiveCart.Lines[index] = s.ActiveCart.Lines[index] with { DiscountPercent = percent };
    });
    public Task AssignCustomerAsync(string? id) => ChangeAsync("cart.customer", id ?? "walk-in", (s, _) =>
    {
        if (id is not null && !s.Customers.Any(c => c.Id == id)) throw new PosException("Customer not found.");
        s.ActiveCart.CustomerId = id;
    });
    public Task AddCustomerAsync(string name, string email) => ChangeAsync("customer.add", "Customer created", (s, _) =>
    {
        name = name.Trim(); email = email.Trim();
        if (name.Length is < 2 or > 100 || email.Length > 200 || (email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(email, out var parsedEmail))) throw new PosException("Enter a customer name and a valid optional email address.");
        if (email.Length > 0 && s.Customers.Any(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase))) throw new PosException("That email address is already registered.");
        s.Customers.Add(new Customer(Guid.NewGuid().ToString("N"), name, email));
    });
    public Task ParkAsync(string label) => ChangeAsync("cart.park", "Sale parked", (s, _) =>
    {
        if (s.ActiveCart.Lines.Count == 0) throw new PosException("Add merchandise before holding a sale.");
        if (s.ParkedCarts.Count >= 100) throw new PosException("Resume or void an existing held sale first.");
        s.ActiveCart.Label = string.IsNullOrWhiteSpace(label) ? $"Held sale {s.ParkedCarts.Count + 1}" : label.Trim()[..Math.Min(label.Trim().Length, 80)];
        s.ParkedCarts.Add(s.ActiveCart); s.ActiveCart = new Cart();
    });
    public Task ResumeAsync(string id) => ChangeAsync("cart.resume", id, (s, _) =>
    {
        if (s.ActiveCart.Lines.Count > 0) throw new PosException("Hold or void the current sale before resuming another.");
        var cart = s.ParkedCarts.FirstOrDefault(x => x.Id == id) ?? throw new PosException("Held sale not found.");
        s.ActiveCart = cart; s.ParkedCarts.Remove(cart);
    });
    public Task VoidAsync(string reason) => ChangeAsync("cart.void", Reason(reason), (s, _) => s.ActiveCart = new Cart());
    public Task<SaleReceipt> CheckoutAsync(string cartId, IReadOnlyList<Tender> tenders) => ChangeAsync("sale.complete", cartId, (s, actor) =>
    {
        var previous = s.Sales.FirstOrDefault(x => x.CartId == cartId);
        if (previous is not null) return previous;
        if (s.ActiveCart.Id != cartId) throw new PosException("The sale has changed. Review it before taking payment.");
        var shift = s.OpenShift ?? throw new PosException("Open a register shift before completing a sale.");
        if (s.ActiveCart.Lines.Count == 0) throw new PosException("The cart is empty.");
        if (tenders.Count > 8) throw new PosException("A sale supports at most eight tender entries.");
        var totals = Pricing.Calculate(s.ActiveCart, s.Currency);
        foreach (var tender in tenders)
        {
            Pricing.Amount(tender.Amount, s.Currency);
            if (tender.Amount <= 0 || !Enum.IsDefined(tender.Kind)) throw new PosException("Invalid tender.");
            if (tender.Kind == TenderKind.SimulatedCard && !tender.Reference.StartsWith("DEMO-", StringComparison.Ordinal)) throw new PosException("This sample only accepts explicitly simulated card authorizations.");
        }
        var paid = tenders.Sum(x => x.Amount);
        if (paid < totals.Total) throw new PosException("The amount tendered does not cover the sale.");
        var change = paid - totals.Total;
        var cash = tenders.Where(x => x.Kind == TenderKind.Cash).Sum(x => x.Amount);
        if (change > cash || tenders.Where(x => x.Kind == TenderKind.SimulatedCard).Sum(x => x.Amount) > totals.Total) throw new PosException("Card overpayment cannot be returned as cash change.");
        foreach (var group in s.ActiveCart.Lines.GroupBy(x => x.ProductId))
        {
            var p = FindProduct(s, group.Key);
            if (!p.Active || p.Stock < group.Sum(x => x.Quantity)) throw new PosException($"Insufficient stock for {p.Name}. Held sales do not reserve stock.");
        }
        var customer = s.Customers.FirstOrDefault(x => x.Id == s.ActiveCart.CustomerId);
        var points = customer is null ? 0 : checked((int)decimal.Floor(totals.Total));
        var sale = new SaleReceipt($"S-{s.Sales.Count + 1:000000}", cartId, DateTimeOffset.UtcNow, actor.Id, actor.Name, shift.Id, customer?.Id, customer?.Name ?? "Walk-in customer", s.Currency, totals.Lines.ToList(), tenders.ToList(), totals.Subtotal, totals.Discount, totals.Net, totals.Tax, totals.Total, change, points);
        foreach (var group in s.ActiveCart.Lines.GroupBy(x => x.ProductId))
        {
            var index = s.Products.FindIndex(x => x.Id == group.Key);
            s.Products[index] = s.Products[index] with { Stock = s.Products[index].Stock - group.Sum(x => x.Quantity) };
        }
        if (customer is not null) s.Customers[s.Customers.IndexOf(customer)] = customer with { LoyaltyPoints = checked(customer.LoyaltyPoints + points) };
        s.Sales.Add(sale); s.ActiveCart = new Cart();
        return sale;
    });
    public Task<ReturnReceipt> ReturnAsync(string saleId, IReadOnlyDictionary<string, decimal> quantities, string reason, string requestId) => ChangeAsync("sale.return", Reason(reason), (s, actor) =>
    {
        Manager(actor);
        if (string.IsNullOrWhiteSpace(requestId)) throw new PosException("A return request identity is required.");
        var prior = s.Returns.FirstOrDefault(x => x.RequestId == requestId);
        if (prior is not null)
        {
            if (prior.SaleId != saleId) throw new PosException("Return identity was already used for a different sale.");
            return prior;
        }
        var shift = s.OpenShift ?? throw new PosException("Open a shift before processing a return.");
        var saleIndex = s.Sales.FindIndex(x => x.Id == saleId);
        if (saleIndex < 0) throw new PosException("Original sale not found.");
        var sale = s.Sales[saleIndex];
        if (quantities.Count == 0) throw new PosException("Select at least one item to return.");
        var lines = quantities.Select(pair => Pricing.Return(sale.Lines.FirstOrDefault(x => x.Id == pair.Key) ?? throw new PosException("Return item is not in the original sale."), pair.Value, s.Currency)).ToList();
        var total = lines.Sum(x => x.Total); var remaining = total;
        var refundTenders = new List<Tender>();
        foreach (var kind in new[] { TenderKind.SimulatedCard, TenderKind.Cash })
        {
            var available = sale.Tenders.Where(t => t.Kind == kind).Sum(t => t.Amount) - (kind == TenderKind.Cash ? sale.Change : 0) - s.Returns.Where(r => r.SaleId == saleId).SelectMany(r => r.RefundTenders).Where(t => t.Kind == kind).Sum(t => t.Amount);
            var allocated = Math.Min(remaining, available);
            if (allocated > 0) { refundTenders.Add(new Tender(kind, allocated, kind == TenderKind.SimulatedCard ? "DEMO-REFUND-" + requestId : "")); remaining -= allocated; }
        }
        if (remaining != 0) throw new PosException("Return exceeds the original payment balance.");
        if (CashExpected(s) < refundTenders.Where(t => t.Kind == TenderKind.Cash).Sum(t => t.Amount)) throw new PosException("The cash drawer does not have enough recorded cash for this refund.");
        var returnedSoFar = s.Returns.Where(r => r.SaleId == saleId).Sum(r => r.Total);
        var reversedSoFar = s.Returns.Where(r => r.SaleId == saleId).Sum(r => r.ReversedPoints);
        var reversed = sale.Total == 0 ? 0 : (int)decimal.Floor(sale.LoyaltyPoints * ((returnedSoFar + total) / sale.Total)) - reversedSoFar;
        var result = new ReturnReceipt($"R-{s.Returns.Count + 1:000000}", requestId, saleId, shift.Id, DateTimeOffset.UtcNow, actor.Id, reason.Trim(), refundTenders, lines, total, lines.Sum(x => x.Tax), reversed);
        foreach (var line in lines)
        {
            var index = sale.Lines.FindIndex(x => x.Id == line.SaleLineId);
            sale.Lines[index] = sale.Lines[index] with { ReturnedQuantity = sale.Lines[index].ReturnedQuantity + line.Quantity };
            var p = s.Products.FindIndex(x => x.Id == line.ProductId);
            if (p >= 0) s.Products[p] = s.Products[p] with { Stock = s.Products[p].Stock + line.Quantity };
        }
        var customerIndex = s.Customers.FindIndex(x => x.Id == sale.CustomerId);
        if (customerIndex >= 0) s.Customers[customerIndex] = s.Customers[customerIndex] with { LoyaltyPoints = Math.Max(0, s.Customers[customerIndex].LoyaltyPoints - reversed) };
        s.Returns.Add(result); return result;
    });
    public Task AdjustStockAsync(string productId, decimal counted, string reason) => ChangeAsync("stock.count", Reason(reason), (s, actor) =>
    {
        Manager(actor); var product = FindProduct(s, productId);
        if (counted < 0 || counted > 1000000 || decimal.Round(counted, product.Weighted ? 3 : 0) != counted) throw new PosException("Enter a valid non-negative stock count.");
        s.Products[s.Products.IndexOf(product)] = product with { Stock = counted };
    });
    public Task OpenShiftAsync(decimal openingFloat) => ChangeAsync("shift.open", "Register opened", (s, actor) =>
    {
        if (s.OpenShift is not null) throw new PosException("A shift is already open.");
        Pricing.Amount(openingFloat, s.Currency);
        s.OpenShift = new Shift(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, actor.Id, openingFloat);
    });
    public Task MoveCashAsync(decimal amount, string reason) => ChangeAsync("cash.move", Reason(reason), (s, actor) =>
    {
        Manager(actor); var shift = s.OpenShift ?? throw new PosException("The register is closed.");
        Pricing.Amount(amount, s.Currency, true);
        if (amount == 0 || CashExpected(s) + amount < 0) throw new PosException("Cash movement must be nonzero and cannot make the drawer negative.");
        s.CashMovements.Add(new CashMovement(Guid.NewGuid().ToString("N"), shift.Id, DateTimeOffset.UtcNow, actor.Id, amount, reason.Trim()));
    });
    public Task CloseShiftAsync(decimal counted) => ChangeAsync("shift.close", "Register reconciled", (s, actor) =>
    {
        Manager(actor); Pricing.Amount(counted, s.Currency);
        var shift = s.OpenShift ?? throw new PosException("The register is already closed.");
        if (s.ActiveCart.Lines.Count > 0) throw new PosException("Hold or void the active sale before closing the register.");
        s.ClosedShifts.Add(shift with { ClosedAt = DateTimeOffset.UtcNow, CountedCash = counted, ExpectedCash = CashExpected(s) });
        s.OpenShift = null;
    });
    public static decimal CashExpected(PosState state)
    {
        if (state.OpenShift is not { } shift) return 0;
        return shift.OpeningFloat + state.Sales.Where(x => x.ShiftId == shift.Id).Sum(x => x.Tenders.Where(t => t.Kind == TenderKind.Cash).Sum(t => t.Amount) - x.Change) - state.Returns.Where(x => x.ShiftId == shift.Id).Sum(x => x.RefundTenders.Where(t => t.Kind == TenderKind.Cash).Sum(t => t.Amount)) + state.CashMovements.Where(x => x.ShiftId == shift.Id).Sum(x => x.Amount);
    }
}
