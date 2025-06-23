#if ANDROID
using Android.Util;
using AndroidX.AppCompat.App;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AviationApp
{
    internal class Utility
    {
#if ANDROID
        public static async Task DisplayAutoDismissAlert(string title, string message, string cancel)
        {
            try
            {
                var context = Android.App.Application.Context;
                var builder = new AlertDialog.Builder(context);
                builder.SetTitle(title);
                builder.SetMessage(message);
                builder.SetNeutralButton(cancel, (s, e) => { });
                var dialog = builder.Create();

                dialog.Show();
                Log.Debug("AutoDismissAlert", "Alert shown");

                // Auto-dismiss after 1 second
                await Task.Delay(1000);
                if (dialog != null && dialog.IsShowing)
                {
                    dialog.Dismiss();
                    Log.Debug("AutoDismissAlert", "Alert dismissed after 1 second");
                }
            }
            catch (Exception ex)
            {
                Log.Error("AutoDismissAlert", $"Alert error: {ex.Message}\n{ex.StackTrace}");
            }
        }
#endif
    }
}
