namespace CubeShelf.Mobile;

public sealed record MobileDeviceStatus(string Time, int? BatteryPercent, bool IsCharging, string Connection)
{
    // Android NetworkInfo subtypes; unknown/restricted radio information must not imply 4G.
    public static string AndroidCellularLabel(int subtype) => subtype switch
    {
        1 or 2 or 4 or 7 or 11 or 16 => "2G",
        3 or 5 or 6 or 8 or 9 or 10 or 12 or 14 or 15 or 17 => "3G",
        13 or 19 => "4G",
        20 => "5G",
        _ => "Mobile"
    };
}
