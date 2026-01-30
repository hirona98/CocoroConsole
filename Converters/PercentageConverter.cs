using System;
using System.Globalization;
using System.Windows.Data;

namespace CocoroConsole.Converters
{
    /// <summary>
    /// 数値を指定比率でスケーリングするコンバーター
    ///
    /// 例:
    /// - 入力: 500, parameter: 0.75 → 出力: 375
    /// - 入力: 500, parameter: 0.9  → 出力: 450
    ///
    /// XAMLのサイズ計算（MaxWidthなど）で、親要素の幅に対する割合を指定する用途を想定します。
    /// </summary>
    public class PercentageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // --- 係数（parameter）を取得 ---
            double ratio = 1.0;
            if (parameter != null && double.TryParse(parameter.ToString(), out var parsedRatio))
            {
                ratio = parsedRatio;
            }

            // --- 数値をスケール ---
            if (value is double doubleValue)
            {
                return doubleValue * ratio;
            }
            else if (value is float floatValue)
            {
                return floatValue * ratio;
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // --- 係数（parameter）を取得 ---
            double ratio = 1.0;
            if (parameter != null && double.TryParse(parameter.ToString(), out var parsedRatio))
            {
                ratio = parsedRatio;
            }

            // --- 逆変換（スケール解除） ---
            if (ratio == 0)
            {
                return value;
            }

            if (value is double doubleValue)
            {
                return doubleValue / ratio;
            }
            else if (value is string stringValue && double.TryParse(stringValue, out double parsedValue))
            {
                return parsedValue / ratio;
            }

            return value;
        }
    }
}
