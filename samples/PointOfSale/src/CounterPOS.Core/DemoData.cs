namespace CounterPOS.Core;
public static class DemoData
{
    /// <summary>All seed customers are fictional. Prices and tax rates are illustrative.</summary>
    public static PosState Create(bool openShift = true) => new()
    {
        Products =
        [
            new("espresso", "CF-001", "100000000001", "Espresso", "Coffee", "ES", 3.50m, .10m, TaxMode.Inclusive, 180),
            new("flat-white", "CF-002", "100000000002", "Flat white", "Coffee", "FW", 4.80m, .10m, TaxMode.Inclusive, 120),
            new("cappuccino", "CF-003", "100000000003", "Cappuccino", "Coffee", "CA", 4.50m, .10m, TaxMode.Inclusive, 140),
            new("iced-latte", "CF-004", "100000000004", "Iced oat latte", "Coffee", "IL", 5.80m, .10m, TaxMode.Inclusive, 64),
            new("croissant", "BK-001", "200000000001", "Butter croissant", "Bakery", "CR", 3.20m, .10m, TaxMode.Inclusive, 38),
            new("cinnamon", "BK-002", "200000000002", "Cinnamon bun", "Bakery", "CB", 4.20m, .10m, TaxMode.Inclusive, 24),
            new("sourdough", "BK-003", "200000000003", "Sourdough loaf", "Bakery", "SL", 7.50m, .05m, TaxMode.Inclusive, 12),
            new("cookie", "BK-004", "200000000004", "Chocolate cookie", "Bakery", "CK", 2.80m, .10m, TaxMode.Inclusive, 48),
            new("oranges", "FR-001", "300000000001", "Seasonal oranges", "Fresh", "OR", 4.00m, .05m, TaxMode.Inclusive, 28.5m, true),
            new("avocado", "FR-002", "300000000002", "Avocado toast", "Fresh", "AT", 9.50m, .10m, TaxMode.Inclusive, 18),
            new("granola", "FR-003", "300000000003", "Berry granola", "Fresh", "BG", 7.20m, .10m, TaxMode.Inclusive, 21),
            new("juice", "FR-004", "300000000004", "Cold-pressed juice", "Fresh", "JU", 5.50m, .10m, TaxMode.Inclusive, 32),
            new("beans", "RT-001", "400000000001", "House blend · 250g", "Retail", "HB", 14.00m, .20m, TaxMode.Inclusive, 24),
            new("mug", "RT-002", "400000000002", "Ceramic cup", "Retail", "CC", 18.00m, .20m, TaxMode.Inclusive, 8),
            new("tote", "RT-003", "400000000003", "Market tote", "Retail", "MT", 12.00m, .20m, TaxMode.Inclusive, 16),
            new("bottle", "RT-004", "400000000004", "Travel flask", "Retail", "TF", 24.00m, .20m, TaxMode.Inclusive, 0)
        ],
        Customers = [new("c-alex", "Taylor Brooks", "taylor@example.test", 120), new("c-sam", "Sam Rivera", "sam@example.test", 48)],
        OpenShift = openShift ? new Shift("shift-demo-001", DateTimeOffset.UtcNow, "manager-demo", 200m) : null
    };
}
