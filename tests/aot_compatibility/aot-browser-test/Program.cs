Console.WriteLine("PuppeteerSharp API Check");

// Check WaitUntilNavigation enum values
var waitUntilValues = Enum.GetNames<WaitUntilNavigation>();
Console.WriteLine($"WaitUntilNavigation values: {string.Join(", ", waitUntilValues)}");

// Check IPage.Evaluate methods
var pageType = typeof(IPage);
var evalMethods = pageType.GetMethods()
    .Where(m => m.Name.Contains("Evaluate"))
    .Select(m => $"{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) -> {m.ReturnType.Name}");
Console.WriteLine($"IPage Evaluate methods: {string.Join("; ", evalMethods)}");

// Check NavigationOptions
var navType = typeof(NavigationOptions);
var navProps = navType.GetProperties();
Console.WriteLine($"NavigationOptions properties: {string.Join(", ", navProps.Select(p => $"{p.Name}: {p.PropertyType.Name}"))}");

// Check BrowserFetcher.DownloadAsync return type
var fetcherType = typeof(BrowserFetcher);
var downloadMethods = fetcherType.GetMethods()
    .Where(m => m.Name == "DownloadAsync")
    .Select(m => $"{m.Name} -> {m.ReturnType.Name}");
Console.WriteLine($"BrowserFetcher.DownloadAsync: {string.Join("; ", downloadMethods)}");

Console.WriteLine("API check complete.");
