using System;
using System.Threading;

namespace DynamicIslandPC.Wpf.Helpers
{
    public static class ClipboardHelper
    {
        public static void RetryAction(Action action, int maxRetries = 5, int delayMs = 50)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception)
                {
                    if (i == maxRetries - 1) throw;
                    Thread.Sleep(delayMs);
                }
            }
        }
    }
}