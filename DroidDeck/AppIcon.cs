using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace DroidDeck
{
    internal static class AppIcon
    {
        private static Icon Load(Size size)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DroidDeck.droiddeck.ico")!;
            return new Icon(stream, size);
        }

        public static Icon Small => Load(SystemInformation.SmallIconSize);

        public static Icon Large => Load(SystemInformation.IconSize);
    }
}
