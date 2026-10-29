namespace RapidShot;

internal sealed class MinimalMenuRenderer : ToolStripProfessionalRenderer
{
    public MinimalMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color MenuBorder => Theme.Raised;
        public override Color MenuItemBorder => Theme.Raised;
        public override Color MenuItemSelected => Theme.Raised;
        public override Color MenuItemSelectedGradientBegin => Theme.Raised;
        public override Color MenuItemSelectedGradientEnd => Theme.Raised;
        public override Color MenuItemPressedGradientBegin => Theme.Raised;
        public override Color MenuItemPressedGradientMiddle => Theme.Raised;
        public override Color MenuItemPressedGradientEnd => Theme.Raised;
    }
}
