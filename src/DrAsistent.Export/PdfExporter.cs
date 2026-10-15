using DrAsistent.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DrAsistent.Export;

public static class PdfExporter
{
    static PdfExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Export(ScheduleDocument doc)
    {
        var p = doc.Problem;
        var days = p.DaysInMonth;

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontSize(7));

            page.Header().Column(col =>
            {
                col.Item().Text(doc.Title).FontSize(13).Bold();
                col.Item().Text(doc.Subtitle).FontSize(9);
                col.Item().PaddingBottom(6);
            });

            page.Content().Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(110);
                        for (var d = 0; d < days; d++) c.RelativeColumn();
                        c.ConstantColumn(30);
                        c.ConstantColumn(30);
                        c.ConstantColumn(30);
                    });

                    table.Header(h =>
                    {
                        h.Cell().Element(HeaderCell).Text("Nume");
                        for (var d = 0; d < days; d++)
                        {
                            var date = p.Date(d);
                            var special = p.Calendar.IsSpecialDay(date);
                            h.Cell().Element(c => HeaderCell(c, special)).AlignCenter()
                                .Text($"{d + 1}\n{ScheduleDocument.DayLetter(date)}");
                        }
                        h.Cell().Element(HeaderCell).AlignCenter().Text("Lucrat");
                        h.Cell().Element(HeaderCell).AlignCenter().Text("Țintă");
                        h.Cell().Element(HeaderCell).AlignCenter().Text("W/S");
                    });

                    foreach (var group in doc.Groups)
                    {
                        table.Cell().ColumnSpan((uint)(days + 4)).Background("#D9E1F2").Padding(2)
                            .Text(group.Key.Label()).Bold();
                        foreach (var r in group)
                        {
                            table.Cell().Element(BodyCell).Text(r.Employee.Name);
                            for (var d = 0; d < days; d++)
                            {
                                var code = doc.Schedule.Get(r.Employee.Id, d);
                                var color = ScheduleDocument.CellColor(code)
                                            ?? (p.Calendar.IsSpecialDay(p.Date(d)) ? ScheduleDocument.SpecialDayColor : "FFFFFF");
                                table.Cell().Element(BodyCell).Background("#" + color).AlignCenter()
                                    .Text(ScheduleDocument.CellText(code));
                            }
                            table.Cell().Element(BodyCell).AlignCenter().Text(r.WorkedHours.ToString());
                            table.Cell().Element(BodyCell).AlignCenter().Text(r.TargetHours.ToString());
                            table.Cell().Element(BodyCell).AlignCenter().Text(r.SpecialHours.ToString());
                        }
                    }
                });

                col.Item().PaddingTop(6).Text(
                    "Legendă: Z = zi 07-19, N = noapte 19-07, Z8 = tură scurtă 07-15, CO = concediu de odihnă, CM = concediu medical, gol = liber. W/S = ore de weekend/sărbători.");
            });

            page.Footer().AlignRight().Text(t =>
            {
                t.Span("Pagina ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer c) => HeaderCell(c, false);

    private static IContainer HeaderCell(IContainer c, bool special) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Medium)
            .Background(special ? "#" + ScheduleDocument.SpecialDayColor : "#F2F2F2")
            .Padding(1).DefaultTextStyle(t => t.Bold());

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingVertical(2).PaddingHorizontal(1);
}
