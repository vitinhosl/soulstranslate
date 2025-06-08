using System;
using System.Drawing;
using System.Windows.Forms;

namespace CustomClass
{
    internal class CustomMenuStrip : MenuStrip
    {
        public CustomMenuStrip()
        {
            BackColor = Color.FromArgb(95, 107, 119);
            // Usa o renderer customizado que define a cor do texto para branco
            Renderer = new CustomRenderer(new CustomColorTable());
        }
    }

    // Renderer customizado que sobrescreve os métodos de renderização dos ícones e separadores.
    internal class CustomRenderer : ToolStripProfessionalRenderer
    {
        // Propriedade para definir a imagem de fundo atrás dos ícones.
        public Image IconBackgroundImage { get; set; }
        // Propriedade para definir a imagem de fundo das linhas separadoras.
        public Image SeparatorBackgroundImage { get; set; }

        public CustomRenderer(CustomColorTable colorTable) : base(colorTable)
        {
        }

        // Sobrescreve a renderização dos ícones para desenhar a imagem de fundo antes do ícone.
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            if (IconBackgroundImage != null && e.Image != null)
            {
                // Desenha a imagem de fundo na área destinada ao ícone.
                e.Graphics.DrawImage(IconBackgroundImage, e.ImageRectangle);
            }
            base.OnRenderItemImage(e);
        }

        // Sobrescreve a renderização dos separadores para desenhar a imagem de fundo.
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (SeparatorBackgroundImage != null)
            {
                Rectangle rect = new Rectangle(Point.Empty, e.Item.Size);
                e.Graphics.DrawImage(SeparatorBackgroundImage, rect);
            }
            else
            {
                base.OnRenderSeparator(e);
            }
        }

        // Mantém a personalização do texto (cor branca).
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.White;
            base.OnRenderItemText(e);
        }
    }

    internal class CustomColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(95, 107, 119);
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(48, 52, 59);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(48, 52, 59);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(75, 87, 89);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(75, 87, 89);
        public override Color MenuBorder => Color.FromArgb(38, 42, 49);
        public override Color ToolStripDropDownBackground => Color.FromArgb(28, 32, 39);
        public override Color ToolStripBorder => Color.Transparent;
        public override Color ImageMarginGradientBegin => Color.FromArgb(38, 42, 49);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(38, 42, 49);
        public override Color ImageMarginGradientEnd => Color.FromArgb(38, 42, 49);
        public override Color SeparatorDark => Color.FromArgb(38, 42, 49);
        public override Color SeparatorLight => Color.FromArgb(38, 42, 49);
    }
}
