using SmartX.Desktop.Forms;

namespace SmartX.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // Windows Forms runs its message loop (Microsoft, n.d.-a).
        Application.Run(new MainMenuForm());
    }
}

// References (Harvard style)
// These sources support the algorithms and platform APIs; Smart-X-specific thresholds,
// simulated links and recommendation rules are application design decisions.
// Math.NET Project (n.d.) 'SimpleRegression.cs'. Math.NET Numerics. Available at: https://github.com/mathnet/mathnet-numerics/blob/master/src/Numerics/LinearRegression/SimpleRegression.cs (Accessed: 6 November 2026).
// Microsoft (n.d.-a) 'Application.Run'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Windows.Forms/Application.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-b) 'Arrays'. Available at: https://github.com/dotnet/docs/blob/main/docs/csharp/language-reference/builtin-types/arrays.md (Accessed: 6 November 2026).
// Microsoft (n.d.-c) 'Asynchronous programming scenarios'. Available at: https://github.com/dotnet/docs/blob/main/docs/csharp/asynchronous-programming/async-scenarios.md (Accessed: 6 November 2026).
// Microsoft (n.d.-d) 'Cancellation in Managed Threads'. Available at: https://github.com/dotnet/docs/blob/main/docs/standard/threading/cancellation-in-managed-threads.md (Accessed: 6 November 2026).
// Microsoft (n.d.-e) 'Control Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Windows.Forms/Control.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-g) 'Dictionary<TKey,TValue> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/Dictionary`2.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-h) 'FileStream Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.IO/FileStream.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-i) 'Form Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Windows.Forms/Form.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-j) 'Generic classes and methods'. Available at: https://github.com/dotnet/docs/blob/main/docs/csharp/fundamentals/types/generics.md (Accessed: 6 November 2026).
// Microsoft (n.d.-k) 'Graphics Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Drawing/Graphics.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-l) 'HashSet<T> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/HashSet`1.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-m) 'HttpClient Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Net.Http/HttpClient.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-n) 'IFormFile Interface'. Available at: https://github.com/dotnet/aspnetcore/blob/main/src/Http/Http.Features/src/IFormFile.cs (Accessed: 6 November 2026).
// Microsoft (n.d.-o) 'JSON serialization and deserialization in .NET'. Available at: https://github.com/dotnet/docs/blob/main/docs/standard/serialization/system-text-json/overview.md (Accessed: 6 November 2026).
// Microsoft (n.d.-p) 'Model validation in ASP.NET Core MVC and Razor Pages'. Available at: https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/mvc/models/validation.md (Accessed: 6 November 2026).
// Microsoft (n.d.-q) 'OpenFileDialog Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Windows.Forms/OpenFileDialog.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-r) 'Operator overloading'. Available at: https://github.com/dotnet/docs/blob/main/docs/csharp/language-reference/operators/operator-overloading.md (Accessed: 6 November 2026).
// Microsoft (n.d.-s) 'PriorityQueue<TElement,TPriority> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/PriorityQueue`2.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-t) 'Queue<T> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/Queue`1.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-u) 'Random Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System/Random.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-v) 'SortedDictionary<TKey,TValue> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/SortedDictionary`2.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-w) 'Stack<T> Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Collections.Generic/Stack`1.xml (Accessed: 6 November 2026).
// Microsoft (n.d.-x) 'The lock statement'. Available at: https://github.com/dotnet/docs/blob/main/docs/csharp/language-reference/statements/lock.md (Accessed: 6 November 2026).
// Microsoft (n.d.-y) 'Timer Class'. Available at: https://github.com/dotnet/dotnet-api-docs/blob/main/xml/System.Windows.Forms/Timer.xml (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-a) 'The AVL Tree'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/SearchStruct/AVL.rst (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-b) 'Binary Search Trees'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/Binary/BST.rst (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-c) 'Graphs Chapter Introduction'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/Graph/GraphIntro.rst (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-d) 'Graph Traversals'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/Graph/GraphTraversal.rst (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-e) 'Shortest Paths Problems'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/Graph/GraphShortest.rst (Accessed: 6 November 2026).
// Shaffer, C. (n.d.-f) 'Minimal Cost Spanning Trees'. OpenDSA, Virginia Tech. Available at: https://github.com/OpenDSA/OpenDSA/blob/master/RST/en/Graph/MCST.rst (Accessed: 6 November 2026).
