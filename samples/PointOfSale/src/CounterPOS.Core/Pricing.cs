namespace CounterPOS.Core;
/// <summary>Decimal, line-level, away-from-zero rounding and largest-remainder order discount allocation.</summary>
public static class Pricing
{
    public static CartTotals Calculate(Cart cart, Currency currency)
    {
        currency.Validate(); Percent(cart.DiscountPercent); Amount(cart.DiscountAmount, currency);
        var preliminary = cart.Lines.Select(line =>
        {
            ValidateLine(line, currency);
            var before = currency.Round(checked(line.UnitPrice * line.Quantity));
            var lineDiscount = currency.Round(before * line.DiscountPercent / 100m);
            return (Line: line, Before: before, After: before - lineDiscount, LineDiscount: lineDiscount);
        }).ToArray();
        var basis = preliminary.Sum(x => x.After);
        var orderDiscount = currency.Round(basis * cart.DiscountPercent / 100m) + cart.DiscountAmount;
        if (orderDiscount > basis) throw new PosException("Discount cannot exceed the merchandise amount.");
        var allocation = Allocate(orderDiscount, preliminary.Select(x => x.After).ToArray(), currency);
        var lines = preliminary.Select((x, i) =>
        {
            var after = x.After - allocation[i];
            var net = x.Line.TaxMode == TaxMode.Inclusive ? currency.Round(after / (1m + x.Line.TaxRate)) : after;
            var tax = x.Line.TaxMode == TaxMode.Inclusive ? after - net : currency.Round(net * x.Line.TaxRate);
            return new PricedLine(x.Line.Id, x.Line.ProductId, x.Line.Sku, x.Line.Name, x.Line.Quantity, x.Line.UnitPrice, x.Line.TaxRate, x.Before, x.LineDiscount + allocation[i], net, tax, net + tax, x.Line.Weighted);
        }).ToArray();
        return new CartTotals(lines, preliminary.Sum(x => x.Before), lines.Sum(x => x.Discount), lines.Sum(x => x.Net), lines.Sum(x => x.Tax), lines.Sum(x => x.Total));
    }
    public static decimal[] Allocate(decimal amount, IReadOnlyList<decimal> weights, Currency currency)
    {
        Amount(amount, currency);
        if (weights.Any(x => x < 0)) throw new PosException("Allocation weights cannot be negative.");
        var sum = weights.Sum();
        if (sum == 0)
        {
            if (amount != 0) throw new PosException("A discount requires an eligible item.");
            return new decimal[weights.Count];
        }
        if (amount > sum) throw new PosException("Allocation exceeds its basis.");
        decimal factor = currency.Digits switch { 0 => 1, 1 => 10, 2 => 100, _ => 1000 };
        var units = amount * factor;
        var raw = weights.Select(w => units * (w / sum)).ToArray();
        var allocated = raw.Select(decimal.Floor).ToArray();
        var remainder = checked((int)(units - allocated.Sum()));
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => raw[i] - allocated[i]).ThenBy(i => i).Take(remainder)) allocated[i]++;
        return allocated.Select(x => x / factor).ToArray();
    }
    /// <summary>Cumulative rounding gives the final return the remaining original minor units.</summary>
    public static ReturnLine Return(PricedLine line, decimal quantity, Currency currency)
    {
        Quantity(quantity, line.Weighted);
        var returned = line.ReturnedQuantity;
        if (returned + quantity > line.Quantity) throw new PosException("Return quantity exceeds the remaining sold quantity.");
        decimal Portion(decimal total) => currency.Round(total * ((returned + quantity) / line.Quantity)) - currency.Round(total * (returned / line.Quantity));
        var total = Portion(line.Total); var tax = Portion(line.Tax);
        return new ReturnLine(line.Id, line.ProductId, quantity, total - tax, tax, total);
    }
    public static void ValidateLine(CartLine line, Currency currency)
    {
        Quantity(line.Quantity, line.Weighted); Amount(line.UnitPrice, currency); Percent(line.DiscountPercent);
        if (line.TaxRate is < 0 or > 1) throw new PosException("Tax rate must be between zero and one.");
        if (!Enum.IsDefined(line.TaxMode)) throw new PosException("Unknown tax mode.");
    }
    public static void Quantity(decimal quantity, bool weighted)
    {
        if (quantity <= 0 || quantity > 10000 || decimal.Round(quantity, weighted ? 3 : 0) != quantity)
            throw new PosException(weighted ? "Quantity must be positive with at most three decimal places." : "Quantity must be a positive whole number.");
    }
    public static void Percent(decimal value)
    {
        if (value is < 0 or > 100 || decimal.Round(value, 2) != value) throw new PosException("Discount must be between 0 and 100 percent, with at most two decimals.");
    }
    public static void Amount(decimal value, Currency currency, bool allowNegative = false)
    {
        if ((!allowNegative && value < 0) || value is < -100000000m or > 100000000m || currency.Round(value) != value)
            throw new PosException($"Amount must use {currency.Digits} decimal places and be within the supported range.");
    }
}
