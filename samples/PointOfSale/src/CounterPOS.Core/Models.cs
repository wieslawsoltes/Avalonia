using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CounterPOS.Core;
public enum OperatorRole { Cashier, Manager }
public enum TenderKind { Cash, SimulatedCard }
public enum TaxMode { Inclusive, Exclusive }
public sealed record Operator(string Id, string Name, OperatorRole Role);
public sealed record Currency(string Code, string Symbol, int Digits)
{
    public static Currency Usd { get; } = new("USD", "$", 2);
    public decimal Round(decimal value) => decimal.Round(value, Digits, MidpointRounding.AwayFromZero);
    public string Format(decimal value) => $"{Symbol}{value.ToString($"N{Digits}", CultureInfo.InvariantCulture)}";
    public void Validate()
    {
        if (Code.Length != 3 || !Code.All(char.IsAsciiLetterUpper) || Digits is < 0 or > 3 || Symbol.Length > 8)
            throw new PosException("Invalid currency configuration.");
    }
}
public sealed record Product(string Id, string Sku, string Barcode, string Name, string Category, string Mark, decimal Price, decimal TaxRate, TaxMode TaxMode, decimal Stock, bool Weighted = false, bool Active = true);
public sealed record Customer(string Id, string Name, string Email, int LoyaltyPoints = 0);
public sealed record CartLine(string Id, string ProductId, string Sku, string Name, decimal UnitPrice, decimal TaxRate, TaxMode TaxMode, bool Weighted, decimal Quantity, decimal DiscountPercent = 0);
public sealed class Cart
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Label { get; set; } = "Walk-in sale";
    public string? CustomerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public List<CartLine> Lines { get; set; } = [];
}
public sealed record PricedLine(string Id, string ProductId, string Sku, string Name, decimal Quantity, decimal UnitPrice, decimal TaxRate, decimal BeforeDiscount, decimal Discount, decimal Net, decimal Tax, decimal Total, bool Weighted, decimal ReturnedQuantity = 0);
public sealed record CartTotals(IReadOnlyList<PricedLine> Lines, decimal Subtotal, decimal Discount, decimal Net, decimal Tax, decimal Total);
public sealed record Tender(TenderKind Kind, decimal Amount, string Reference = "");
public sealed record SaleReceipt(string Id, string CartId, DateTimeOffset CreatedAt, string OperatorId, string OperatorName, string ShiftId, string? CustomerId, string CustomerName, Currency Currency, List<PricedLine> Lines, List<Tender> Tenders, decimal Subtotal, decimal Discount, decimal Net, decimal Tax, decimal Total, decimal Change, int LoyaltyPoints);
public sealed record ReturnLine(string SaleLineId, string ProductId, decimal Quantity, decimal Net, decimal Tax, decimal Total);
public sealed record ReturnReceipt(string Id, string RequestId, string SaleId, string ShiftId, DateTimeOffset CreatedAt, string OperatorId, string Reason, List<Tender> RefundTenders, List<ReturnLine> Lines, decimal Total, decimal Tax, int ReversedPoints);
public sealed record CashMovement(string Id, string ShiftId, DateTimeOffset CreatedAt, string OperatorId, decimal Amount, string Reason);
public sealed record Shift(string Id, DateTimeOffset OpenedAt, string OpenedBy, decimal OpeningFloat, DateTimeOffset? ClosedAt = null, decimal? CountedCash = null, decimal? ExpectedCash = null);
public sealed record AuditEntry(long Sequence, DateTimeOffset At, string OperatorId, string Action, string Detail);
/// <summary>Versioned persistence DTO. Services never expose their mutable working copy.</summary>
public sealed class PosState
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public Currency Currency { get; set; } = Currency.Usd;
    public List<Product> Products { get; set; } = [];
    public List<Customer> Customers { get; set; } = [];
    public Cart ActiveCart { get; set; } = new();
    public List<Cart> ParkedCarts { get; set; } = [];
    public List<SaleReceipt> Sales { get; set; } = [];
    public List<ReturnReceipt> Returns { get; set; } = [];
    public Shift? OpenShift { get; set; }
    public List<Shift> ClosedShifts { get; set; } = [];
    public List<CashMovement> CashMovements { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];
}
public sealed class PosException(string message) : Exception(message) { }
public sealed class ConcurrencyException() : Exception("The register changed in another session. Refresh and try again.") { }
public static class StateJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }, WriteIndented = false, MaxDepth = 64
    };
    public static string Serialize(PosState state) => JsonSerializer.Serialize(state, Options);
    public static PosState Deserialize(string json)
    {
        if (json.Length > 32 * 1024 * 1024) throw new PosException("Register data exceeds the local store limit.");
        var state = JsonSerializer.Deserialize<PosState>(json, Options) ?? throw new PosException("Register data is empty.");
        if (state.SchemaVersion != 1) throw new PosException("Unsupported register schema. Data has not been overwritten.");
        state.Currency.Validate();
        if (state.Revision < 0 || state.Products is null || state.ActiveCart is null || state.Sales is null || state.Returns is null || state.ParkedCarts is null || state.Customers is null || state.Audit is null || state.CashMovements is null || state.ClosedShifts is null || state.ActiveCart.Lines is null)
            throw new PosException("Invalid register state. Data has not been overwritten.");
        return state;
    }
    public static PosState Clone(PosState state) => Deserialize(Serialize(state));
}
