using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;


public static class Utils
{
    public const double Delta = 1e-8;
    public static List<string> GetListFromLine(string line)
    {
        return line.Trim().Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public static string GetLineFromList(List<string> listExchanges)
    {
        string line = "";
        foreach (var exchange in listExchanges)
        {
            line += exchange + ", ";
        }

        return line != "" ? line.Substring(0, line.Length - 2) : line;
    }

    public static bool CompareDouble(double first, double second)
    {
        return Math.Abs(first - second) < Delta;
    }

    public static string TimeInString(DateTime executionTime)
    {
        return AddZeroInNumber(executionTime.Year, 3) + "-" + AddZeroInNumber(executionTime.Month, 1) + "-" + AddZeroInNumber(executionTime.Day, 1) + " "
               + AddZeroInNumber(executionTime.Hour, 1) + ":" + AddZeroInNumber(executionTime.Minute, 1) + ":" + AddZeroInNumber(executionTime.Second, 1) + "." +
               AddZeroInNumber(executionTime.Millisecond, 2);
    }

    public static string AddZeroInNumber(int number, int exp)
    {
        for (int i = 1; i <= exp; i++)
        {
            if (number < Math.Pow(10, i))
            {
                string lineZero = "0";
                while (lineZero.Length <= exp - i)
                    lineZero += "0";
                return lineZero + number;
            }
        }
        return number.ToString();
    }
}