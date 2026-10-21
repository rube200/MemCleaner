using System.IO;
using System.Linq;
using System.Security;
using Windows.Win32.System.TaskScheduler;

namespace MemCleaner;

internal static unsafe class StartupTask
{
    private const string RootFolder = @"\";
    private const int NoFlags = 0;
    private const int FirstAction = 1;

    private const string DefinitionResource = "StartupTask.xml";
    private const string UserIdPlaceholder = "{UserId}";
    private const string CommandPlaceholder = "{Command}";

    private static readonly string
        TaskName = $"{TrayApp.AppName} ({Environment.UserDomainName}-{Environment.UserName})";

    private static string UserId => $@"{Environment.UserDomainName}\{Environment.UserName}";

    public static (bool Enabled, string? Warning) CheckAtStartup()
    {
        if (!TryReadRegisteredCommand(out var registeredCommand))
            return (false, null);

        var command = CurrentCommand();
        if (registeredCommand is null || string.Equals(registeredCommand, command, StringComparison.OrdinalIgnoreCase))
            return (true, null);

        if (UnsafeLocationReason(command) is { } reason)
            return (true, $"Start with Windows still starts {TrayApp.AppName} from {registeredCommand}. {reason}");

        Enable();
        return (true, null);
    }

    public static string? LocationProblem()
    {
        return UnsafeLocationReason(CurrentCommand());
    }

    public static void Enable()
    {
        var definitionText = Definition();
        using var folder = OpenRootFolder();
        using var name = PInvoke.SysAllocString(TaskName);
        using var definition = PInvoke.SysAllocString(definitionText);

        IRegisteredTask* registered;
        folder.Pointer->RegisterTask(name, definition, (int)TASK_CREATION.TASK_CREATE_OR_UPDATE, default,
            default, TASK_LOGON_TYPE.TASK_LOGON_INTERACTIVE_TOKEN, default, &registered);
        using var task = new ComRef<IRegisteredTask>(registered);
    }

    public static void Disable()
    {
        using var folder = OpenRootFolder();
        using var name = PInvoke.SysAllocString(TaskName);
        folder.Pointer->DeleteTask(name, NoFlags);
    }

    private static bool TryReadRegisteredCommand(out string? command)
    {
        command = null;
        using var folder = OpenRootFolder();
        using var name = PInvoke.SysAllocString(TaskName);

        IRegisteredTask* registered;
        try
        {
            folder.Pointer->GetTask(name, &registered);
        }
        catch (FileNotFoundException)
        {
            return false;
        }

        using var task = new ComRef<IRegisteredTask>(registered);
        using var definition = new ComRef<ITaskDefinition>(task.Pointer->Definition);
        using var actions = new ComRef<IActionCollection>(definition.Pointer->Actions);
        if (actions.Pointer->Count < FirstAction)
            return true;

        IAction* first;
        actions.Pointer->get_Item(FirstAction, &first);
        using var action = new ComRef<IAction>(first);
        if (action.Pointer->QueryInterface<IExecAction>(out var exec).Failed)
            return true;

        using var execAction = new ComRef<IExecAction>(exec);
        var path = execAction.Pointer->Path;
        try
        {
            command = path.ToString();
        }
        finally
        {
            Marshal.FreeBSTR(path);
        }

        return true;
    }

    private static ComRef<ITaskFolder> OpenRootFolder()
    {
        TaskScheduler.CreateInstance<ITaskService>(out var created).ThrowOnFailure();
        using var service = new ComRef<ITaskService>(created);
        using var path = PInvoke.SysAllocString(RootFolder);
        service.Pointer->Connect(default, default, default, default);

        ITaskFolder* folder;
        service.Pointer->GetFolder(path, &folder);
        return new ComRef<ITaskFolder>(folder);
    }

    private static string Definition()
    {
        var command = CurrentCommand();
        if (UnsafeLocationReason(command) is { } reason)
            throw new InvalidOperationException(reason);

        using var stream = typeof(StartupTask).Assembly.GetManifestResourceStream(DefinitionResource)
                           ?? throw new InvalidOperationException($"{DefinitionResource} is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Replace(UserIdPlaceholder, SecurityElement.Escape(UserId))
            .Replace(CommandPlaceholder, SecurityElement.Escape(command));
    }

    private static string CurrentCommand()
    {
        return Environment.ProcessPath ??
               throw new InvalidOperationException("Could not find the path of this executable.");
    }

    private static string? UnsafeLocationReason(string command)
    {
        var path = Path.GetFullPath(command);
        if (new[]
                { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }.Select(folder =>
                Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(folder)) +
                Path.DirectorySeparatorChar).Any(root =>
                root.Length > 1 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            return null;

        return $"It runs as administrator at logon, so {TrayApp.AppName} has to be in Program Files, where other " +
               $"programs can't replace it. Move it from {Path.GetDirectoryName(path)} to Program Files first.";
    }
}