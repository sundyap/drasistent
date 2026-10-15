using ClosedXML.Excel;
using DrAsistent.Core;

namespace DrAsistent.Export;

public static class ExcelExporter
{
    public static byte[] Export(ScheduleDocument doc)
    {
        using var wb = new XLWorkbook();
        BuildSchedule(wb.AddWorksheet("Grafic"), doc);
        BuildReport(wb.AddWorksheet("Raport"), doc);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildSchedule(IXLWorksheet ws, ScheduleDocument doc)
    {
        var p = doc.Problem;
        var days = p.DaysInMonth;
        const int firstDayCol = 2;
        var totalCol = firstDayCol + days;

        ws.Cell(1, 1).Value = doc.Title;
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = doc.Subtitle;

        const int headerRow = 4;
        ws.Cell(headerRow, 1).Value = "Nume";
        ws.Cell(headerRow + 1, 1).Value = "";
        for (var d = 0; d < days; d++)
        {
            var date = p.Date(d);
            ws.Cell(headerRow, firstDayCol + d).Value = d + 1;
            ws.Cell(headerRow + 1, firstDayCol + d).Value = ScheduleDocument.DayLetter(date);
            if (p.Calendar.IsSpecialDay(date))
                ws.Range(headerRow, firstDayCol + d, headerRow + 1, firstDayCol + d).Style.Fill.BackgroundColor = XLColor.FromHtml("#" + ScheduleDocument.SpecialDayColor);
        }
        string[] totals = ["Lucrat", "Țintă", "Sold", "Ore W/S"];
        for (var i = 0; i < totals.Length; i++) ws.Cell(headerRow, totalCol + i).Value = totals[i];
        var header = ws.Range(headerRow, 1, headerRow + 1, totalCol + totals.Length - 1);
        header.Style.Font.SetBold().Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        var row = headerRow + 2;
        foreach (var group in doc.Groups)
        {
            ws.Cell(row, 1).Value = group.Key.Label();
            ws.Range(row, 1, row, totalCol + totals.Length - 1).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#D9E1F2"));
            row++;
            foreach (var r in group)
            {
                ws.Cell(row, 1).Value = r.Employee.Name;
                for (var d = 0; d < days; d++)
                {
                    var code = doc.Schedule.Get(r.Employee.Id, d);
                    var cell = ws.Cell(row, firstDayCol + d);
                    cell.Value = ScheduleDocument.CellText(code);
                    var color = ScheduleDocument.CellColor(code) ?? (p.Calendar.IsSpecialDay(p.Date(d)) ? ScheduleDocument.SpecialDayColor : null);
                    if (color is not null) cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + color);
                }
                ws.Cell(row, totalCol).Value = r.WorkedHours;
                ws.Cell(row, totalCol + 1).Value = r.TargetHours;
                ws.Cell(row, totalCol + 2).Value = r.Balance;
                ws.Cell(row, totalCol + 3).Value = r.SpecialHours;
                row++;
            }
        }

        var table = ws.Range(headerRow, 1, row - 1, totalCol + totals.Length - 1);
        table.Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Range(headerRow, firstDayCol, row - 1, totalCol + totals.Length - 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        row++;
        ws.Cell(row, 1).Value = "Legendă: Z = zi 07-19, N = noapte 19-07, Z8 = tură scurtă 07-15, CO = concediu de odihnă, CM = concediu medical, gol = liber";

        ws.Column(1).Width = 28;
        for (var d = 0; d < days; d++) ws.Column(firstDayCol + d).Width = 4.2;
        for (var i = 0; i < totals.Length; i++) ws.Column(totalCol + i).Width = 8;
        ws.SheetView.FreezeRows(headerRow + 1);
        ws.SheetView.FreezeColumns(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.FitToPages(1, 0);
    }

    private static void BuildReport(IXLWorksheet ws, ScheduleDocument doc)
    {
        string[] headers = ["Nume", "Categorie", "Normă bază", "Zile CO/CM", "Report lună ant.", "Țintă", "Lucrat", "Sold", "Ore W/S", "Ore W/S (la normă întreagă)", "Ture Z", "Ture N", "Ture Z8"];
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Range(1, 1, 1, headers.Length).Style.Font.SetBold();

        var row = 2;
        foreach (var r in doc.Groups.SelectMany(g => g))
        {
            ws.Cell(row, 1).Value = r.Employee.Name;
            ws.Cell(row, 2).Value = r.Employee.Category.Label();
            ws.Cell(row, 3).Value = r.BaseNormHours;
            ws.Cell(row, 4).Value = r.LeaveWorkingDays;
            ws.Cell(row, 5).Value = r.CarryIn;
            ws.Cell(row, 6).Value = r.TargetHours;
            ws.Cell(row, 7).Value = r.WorkedHours;
            ws.Cell(row, 8).Value = r.Balance;
            ws.Cell(row, 9).Value = r.SpecialHours;
            ws.Cell(row, 10).Value = Math.Round(r.NormalizedSpecialHours, 1);
            ws.Cell(row, 11).Value = r.DayShifts;
            ws.Cell(row, 12).Value = r.NightShifts;
            ws.Cell(row, 13).Value = r.ShortShifts;
            row++;
        }
        ws.Range(1, 1, row - 1, headers.Length).Style.Border.SetInsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorder(XLBorderStyleValues.Thin);
        ws.Columns().AdjustToContents();
    }
}
