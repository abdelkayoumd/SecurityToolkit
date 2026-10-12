using System.ComponentModel.Composition;
using SecurityToolkit.Resources;
using SecurityToolkit.UI;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace SecurityToolkit
{
    [Export(typeof(IXrmToolBoxPlugin))]
    [ExportMetadata("Name", "Security Toolkit")]
    [ExportMetadata("Description", "One place for security administration: users, teams, business units and security roles.")]
    [ExportMetadata("SmallImageBase64", PluginImages.SmallImageBase64)]
    [ExportMetadata("BigImageBase64", PluginImages.BigImageBase64)]
    [ExportMetadata("BackgroundColor", "White")]
    [ExportMetadata("PrimaryFontColor", "Black")]
    [ExportMetadata("SecondaryFontColor", "Gray")]
    public class Plugin : PluginBase
    {
        public override IXrmToolBoxPluginControl GetControl()
        {
            return new PluginControl();
        }
    }
}
