using QtSharp.Charts;

namespace QtSharp.Charts.Demos
{
    /// <summary>
    /// The bar sets the Qt gallery's five bar widgets all share - the same five people and the same
    /// six months - so the ports differ only in the series they are handed to and in which way round
    /// the chart is laid out.
    /// </summary>
    internal static class BarChartData
    {
        internal static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun" };

        internal static readonly string[] Names = { "Jane", "John", "Axel", "Mary", "Samantha" };

        internal static readonly double[][] Values =
        {
            new double[] { 1, 2, 3, 4, 5, 6 },
            new double[] { 5, 0, 0, 4, 0, 7 },
            new double[] { 3, 5, 8, 13, 8, 5 },
            new double[] { 5, 6, 7, 3, 4, 5 },
            new double[] { 9, 7, 5, 3, 1, 2 },
        };

        /// <summary>Builds the five sets, appends them to the series, and hands them back.</summary>
        internal static void AddSets(QAbstractBarSeries series,
            out QBarSet set0, out QBarSet set1, out QBarSet set2, out QBarSet set3, out QBarSet set4)
        {
            var sets = new QBarSet[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                sets[i] = new QBarSet(Names[i]);
                sets[i].Append(Values[i]);
                series.Append(sets[i]);
            }
            set0 = sets[0];
            set1 = sets[1];
            set2 = sets[2];
            set3 = sets[3];
            set4 = sets[4];
        }
    }
}
