namespace DrAsistent.Core;

/// <summary>
/// Sarbatorile legale din Romania (Codul Muncii, art. 139), folosite ca propunere.
/// Utilizatorul le poate modifica in aplicatie daca legea se schimba.
/// </summary>
public static class RomanianHolidays
{
    public static IReadOnlyList<(DateOnly Date, string Name)> For(int year)
    {
        var easter = OrthodoxEaster(year);
        var list = new List<(DateOnly, string)>
        {
            (new(year, 1, 1), "Anul Nou"),
            (new(year, 1, 2), "Anul Nou"),
            (new(year, 1, 6), "Boboteaza"),
            (new(year, 1, 7), "Sf. Ioan Botezătorul"),
            (new(year, 1, 24), "Ziua Unirii Principatelor"),
            (easter.AddDays(-2), "Vinerea Mare"),
            (easter, "Paștele"),
            (easter.AddDays(1), "Paștele (a doua zi)"),
            (new(year, 5, 1), "Ziua Muncii"),
            (new(year, 6, 1), "Ziua Copilului"),
            (easter.AddDays(49), "Rusaliile"),
            (easter.AddDays(50), "Rusaliile (a doua zi)"),
            (new(year, 8, 15), "Adormirea Maicii Domnului"),
            (new(year, 11, 30), "Sf. Andrei"),
            (new(year, 12, 1), "Ziua Națională"),
            (new(year, 12, 25), "Crăciunul"),
            (new(year, 12, 26), "Crăciunul (a doua zi)"),
        };
        // Daca o sarbatoare mobila cade peste una fixa (ex. 1 iunie = Rusalii), pastram o singura intrare.
        return list.GroupBy(h => h.Item1).Select(g => g.First()).OrderBy(h => h.Item1).ToList();
    }

    /// <summary>Pastele ortodox (algoritmul Meeus pentru calendarul iulian, convertit in gregorian).</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = (d + e + 114) % 31 + 1;
        var julianToGregorian = year / 100 - year / 400 - 2;
        return new DateOnly(year, month, day).AddDays(julianToGregorian);
    }
}
