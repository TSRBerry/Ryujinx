using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using Ryujinx.Ava.Common.Locale;
using Ryujinx.Ava.UI.Helpers;
using Ryujinx.Ava.UI.Models;
using System.Threading.Tasks;

namespace Ryujinx.Ava.UI.Controls
{
    public partial class InputDialog : UserControl
    {
        public string Message { get; set; }
        public string Input { get; set; }
        public string SubMessage { get; set; }

        public uint MaxLength { get; }

        public InputDialog(string message, string input = "", string subMessage = "", uint maxLength = int.MaxValue)
        {
            Message = message;
            Input = input;
            SubMessage = subMessage;
            MaxLength = maxLength;

            DataContext = this;
        }

        public InputDialog()
        {
            InitializeComponent();
        }

        public static async Task<(UserResult Result, string Input)> ShowInputDialog(string title, string message,
            string input = "", string subMessage = "", uint maxLength = int.MaxValue)
        {
            UserResult result = UserResult.Cancel;

            InputDialog content = new(message, input, subMessage, maxLength);
            ContentDialog contentDialog = new()
            {
                Title = title,
                PrimaryButtonText = LocaleManager.Instance[LocaleKeys.InputDialogOk],
                SecondaryButtonText = "",
                CloseButtonText = LocaleManager.Instance[LocaleKeys.InputDialogCancel],
                Content = content,
                PrimaryButtonCommand = MiniCommand.Create(() =>
                {
                    result = UserResult.Ok;
                    input = content.Input;
                })
            };
            await contentDialog.ShowAsync();

            return (result, input);
        }
    }
}