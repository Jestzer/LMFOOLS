namespace LMFOOLS.ViewModels;

public class MainViewModel : ViewModelBase
{
    public static string PackageVersion => GetPackageVersion();

    private static string GetPackageVersion()
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly?.GetName().Version?.ToString();
        return version ?? "Error getting version number.";
    }
}