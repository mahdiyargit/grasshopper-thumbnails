using System;
using GrasshopperFilePreview;

// Internal worker for the installer. All user interaction belongs to the wizard.
static class App
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && (args[0] == "--install-managed" || args[0] == "--install-managed-all-users"))
            {
                Registration.Install(true, args[1], args[0] == "--install-managed-all-users");
                return 0;
            }
            if (args.Length == 2 && (args[0] == "--rollback-managed" || args[0] == "--rollback-managed-all-users"))
            {
                Registration.RollbackInstall(args[1], args[0] == "--rollback-managed-all-users");
                return 0;
            }
            if (args.Length != 1)
                return 2;
            switch (args[0])
            {
                case "--install":
                    Registration.Install();
                    break;
                case "--uninstall":
                    Registration.Uninstall();
                    break;
                case "--uninstall-all-users":
                    Registration.Uninstall(true);
                    break;
                case "--refresh-cache":
                    Registration.RefreshThumbnails();
                    break;
                default:
                    return 2;
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }
}
