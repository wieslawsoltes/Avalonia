using System.Globalization;
using System.Text;
namespace CounterPOS.Core;
public sealed record SalesSummary(int SalesCount, int ReturnsCount, decimal GrossSales, decimal Refunds, decimal NetRevenue, decimal Tax, decimal AverageSale);
public static class Reporting
{
    public static SalesSummary Summarize(PosState state)
    {
        var sales = state.Sales.Sum(s => s.Total); var refunds = state.Returns.Sum(r => r.Total);
        return new(state.Sales.Count, state.Returns.Count, sales, refunds, sales - refunds, state.Sales.Sum(s => s.Tax) - state.Returns.Sum(r => r.Tax), state.Sales.Count == 0 ? 0 : state.Currency.Round(sales / state.Sales.Count));
    }
    public static string Receipt(SaleReceipt receipt)
    {
        var c = receipt.Currency; var text = new StringBuilder();
        text.AppendLine("THE DAILY MARKET").AppendLine("CounterPOS · demonstration receipt");
        text.AppendLine("NOT A FISCAL RECEIPT · NO LIVE CARD CHARGES");
        text.AppendLine(new string('─', 42)).AppendLine(receipt.Id);
        text.AppendLine(receipt.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        text.AppendLine($"Cashier: {receipt.OperatorName}").AppendLine($"Customer: {receipt.CustomerName}");
        foreach (var line in receipt.Lines)
        {
            text.AppendLine(new string('─', 42)); text.AppendLine(line.Name);
            text.AppendLine($"{line.Quantity:0.###} × {c.Format(line.UnitPrice)}    {c.Format(line.Total)}");
            if (line.Discount != 0) text.AppendLine($"Discount: {c.Format(line.Discount)}");
            if (line.ReturnedQuantity != 0) text.AppendLine($"Returned quantity: {line.ReturnedQuantity:0.###}");
        }
        text.AppendLine(new string('─', 42));
        text.AppendLine($"Merchandise         {c.Format(receipt.Subtotal)}");
        text.AppendLine($"Discount            {c.Format(receipt.Discount)}");
        text.AppendLine($"Net                 {c.Format(receipt.Net)}");
        text.AppendLine($"Tax                 {c.Format(receipt.Tax)}");
        text.AppendLine($"TOTAL ({c.Code})         {c.Format(receipt.Total)}");
        foreach (var tax in receipt.Lines.GroupBy(l => l.TaxRate).OrderBy(g => g.Key)) text.AppendLine($"Tax {tax.Key:P0}: net {c.Format(tax.Sum(l => l.Net))}, tax {c.Format(tax.Sum(l => l.Tax))}");
        foreach (var tender in receipt.Tenders) text.AppendLine($"{(tender.Kind == TenderKind.Cash ? "Cash" : "SIMULATED CARD")}: {c.Format(tender.Amount)}");
        text.AppendLine($"Change: {c.Format(receipt.Change)}");
        if (receipt.LoyaltyPoints != 0) text.AppendLine($"Loyalty points earned: {receipt.LoyaltyPoints}");
        return text.AppendLine().AppendLine("Thank you. See you again soon.").ToString();
    }
    public static string Csv(PosState state)
    {
        static string Cell(string value)
        {
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
        var b = new StringBuilder("Type,Reference,Timestamp,Customer,Currency,Net,Tax,Total\r\n");
        foreach (var s in state.Sales) b.AppendLine(string.Join(',', "Sale", Cell(s.Id), Cell(s.CreatedAt.ToString("O")), Cell(s.CustomerName), s.Currency.Code, s.Net.ToString(CultureInfo.InvariantCulture), s.Tax.ToString(CultureInfo.InvariantCulture), s.Total.ToString(CultureInfo.InvariantCulture)));
        foreach (var r in state.Returns) b.AppendLine(string.Join(',', "Return", Cell(r.Id), Cell(r.CreatedAt.ToString("O")), Cell(r.SaleId), state.Currency.Code, (r.Tax - r.Total).ToString(CultureInfo.InvariantCulture), (-r.Tax).ToString(CultureInfo.InvariantCulture), (-r.Total).ToString(CultureInfo.InvariantCulture)));
        return b.ToString();
    }
}
