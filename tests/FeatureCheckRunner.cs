using System;

namespace Deskmate
{
    internal static class FeatureCheckRunner
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                Console.Error.WriteLine("Usage: FeatureChecks.exe <TaskNoteImagesCheck|NotificationEmojiCheck|PetLinkCheck|IngatCheck>");
                return 2;
            }

            string checkName = args[0].Trim();
            try
            {
                string summary;
                switch (checkName)
                {
                    case "TaskNoteImagesCheck":
                        summary = TaskNoteImagesCheck.Run();
                        break;
                    case "NotificationEmojiCheck":
                        summary = NotificationEmojiCheck.Run();
                        break;
                    case "PetLinkCheck":
                        summary = PetLinkCheck.Run();
                        break;
                    case "IngatCheck":
                        summary = IngatCheck.Run();
                        break;
                    default:
                        Console.Error.WriteLine("Unknown feature check: " + checkName);
                        return 2;
                }

                if (!string.IsNullOrEmpty(summary))
                {
                    Console.WriteLine(summary);
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
