using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Text;
using System.Net;
using System.Runtime.InteropServices;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace SoulsTranslate
{
    public partial class SoulsTranslate : Form
    {
        private const int EM_SETCUEBANNER = 0x1501;
        const int EM_GETSCROLLPOS = 0x0400 + 221;
        const int EM_SETSCROLLPOS = 0x0400 + 222;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

        private List<(string Id, string Value, int Line)> firstList;
        private List<(string Id, string Value, int Line)> secondList;
        private Dictionary<string, string> firstDict;
        private Dictionary<string, string> secondDict;

        private void SetupGutter(RichTextBox main, RichTextBox gutter)
        {
            gutter.ReadOnly = true;
            gutter.BorderStyle = BorderStyle.None;
            gutter.Font = main.Font;

            main.TextChanged += (_, __) => UpdateGutter(main, gutter);
            main.VScroll += (_, __) => SyncScroll(main, gutter);
            main.Resize += (_, __) => { UpdateGutter(main, gutter); SyncScroll(main, gutter); };

            UpdateGutter(main, gutter);
        }

        private void UpdateGutter(RichTextBox main, RichTextBox gutter)
        {
            if (main.TextLength == 0)
            {
                gutter.Lines = Array.Empty<string>();
            }
            else
            {
                int total = main.GetLineFromCharIndex(main.TextLength) + 1;
                gutter.Lines = Enumerable.Range(1, total).Select(n => n.ToString()).ToArray();
            }
            SyncScroll(main, gutter);
        }

        private void SyncScroll(RichTextBox main, RichTextBox gutter)
        {
            Point pt = new Point();
            SendMessage(main.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref pt);
            SendMessage(gutter.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pt);
        }

        public SoulsTranslate()
        {
            InitializeComponent();
            txtFirst.MaxLength = int.MaxValue;
            txtSecond.MaxLength = int.MaxValue;
            txtComparison.MaxLength = int.MaxValue;

            txtFirst.AllowDrop = true;
            txtSecond.AllowDrop = true;
            txtComparison.AllowDrop = true;

            txtFirst.DragEnter += RichTextBox_DragEnter;
            txtFirst.DragDrop += TxtFirst_DragDrop;
            txtSecond.DragEnter += RichTextBox_DragEnter;
            txtSecond.DragDrop += TxtSecond_DragDrop;
            txtComparison.DragEnter += RichTextBox_DragEnter;

            txtFirst.TextChanged += TxtFirst_TextChanged;
            txtSecond.TextChanged += TxtSecond_TextChanged;
            txtComparison.TextChanged += TxtComparison_TextChanged;

            SetupGutter(txtFirst, txtFirstNumber);
            SetupGutter(txtSecond, txtSecondNumber);
            //SetupGutter(txtComparison, txtComparisonNumber);

            txtComparison.VScroll += (s, e) => SyncScroll(txtComparison, txtComparisonNumber);
            txtComparison.Resize += (s, e) => SyncScroll(txtComparison, txtComparisonNumber);
            txtComparison.TextChanged += (s, e) => SyncScroll(txtComparison, txtComparisonNumber);

            txtSearchFirst.AutoSize = false;
            txtSearchFirst.Height = 31;
            SendMessage(txtSearchFirst.Handle, EM_SETCUEBANNER, (IntPtr)1, "Procurar (CTRL + F)");
        }

        private string BuildRtf(string path, HashSet<string> highlightIds)
        {
            var lines = File.ReadAllLines(path);
            var sb = new StringBuilder();

            // (1) Cabeçalho RTF + tabela de cores, incluindo \\red0\\green100\\blue0; como cor 12
            sb.Append(@"{\rtf1\ansi\deff0");
            sb.Append(@"{\colortbl;");
            sb.Append(@"\red255\green255\blue255;"); //  1 = white
            sb.Append(@"\red255\green121\blue198;"); //  2 = rose
            sb.Append(@"\red69\green250\blue93;");   //  3 = green
            sb.Append(@"\red241\green236\blue94;");  //  4 = yellow
            sb.Append(@"\red150\green150\blue150;"); //  5 = dim (%null%)
            sb.Append(@"\red128\green128\blue128;"); //  6 = gray (números de linha)
            sb.Append(@"\red189\green147\blue249;"); //  7 = prefixo/sufixo (&lt;? … ?&gt;)
            sb.Append(@"\red255\green190\blue241;"); //  8 = antes do '@'
            sb.Append(@"\red243\green214\blue255;"); //  9 = depois do '@'
            sb.Append(@"\red255\green255\blue0;");   // 10 = yellow (header primário)
            sb.Append(@"\red0\green255\blue255;");   // 11 = cyan (header secundário)
            sb.Append(@"\red0\green100\blue0;");     // 12 = green (fundo destacado)
            sb.Append("}");
            sb.Append(@"\cf1 ");

            // (2) Regexs para XML (como antes)
            var rxXml = new Regex(@"^<\?xml\s+(.*?)\?>$", RegexOptions.Compiled);
            var rxNum = new Regex(@"^(</?(entries|fmg)>|<(compression|version|bigendian)>.*?</\3>)$", RegexOptions.Compiled);
            var rxText = new Regex(@"^<text\s+id=""([^""]*)""\s*>([\s\S]*?)</text>$", RegexOptions.Compiled);
            var rxInlineTag = new Regex(@"(&lt;\?)(.*?)(\?\&gt;)", RegexOptions.Compiled);

            // (3) Monta o RTF linha a linha
            for (int i = 0; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string t = rawLine.Trim();

                // 3.1) Declaração <?xml … ?>
                var mXml = rxXml.Match(t);
                if (mXml.Success)
                {
                    sb.Append(@"\cf2 <?xml \cf1 ");
                    foreach (Match a in new Regex(@"(\w+)=(\""[^\""]*\"")", RegexOptions.Compiled)
                                         .Matches(mXml.Groups[1].Value))
                    {
                        sb.Append(@"\cf3 ").Append(EscapeRtf(a.Groups[1].Value))
                          .Append(@"\cf1=").Append(@"\cf4 ").Append(EscapeRtf(a.Groups[2].Value))
                          .Append(@"\cf1 ");
                    }
                    sb.Append(@"\cf2 ?>\cf1\line");
                    continue;
                }

                // 3.2) <entries> / </entries> / <compression>…</compression>
                if (rxNum.IsMatch(t))
                {
                    var m = Regex.Match(t, @"^<(compression|version|bigendian)>(.*?)</\1>$");
                    if (m.Success)
                    {
                        var tag = m.Groups[1].Value;
                        var inner = m.Groups[2].Value;
                        sb.Append(@"\cf2 ").Append(EscapeRtf($"<{tag}>"))
                          .Append(@"\cf1 ").Append(EscapeRtf(inner))
                          .Append(@"\cf2 ").Append(EscapeRtf($"</{tag}>"))
                          .Append(@"\cf1\line");
                    }
                    else
                    {
                        sb.Append(@"\cf2 ").Append(EscapeRtf(t)).Append(@"\cf1\line");
                    }
                    continue;
                }

                // 3.3) <text id="...">…</text>
                if (t.StartsWith("<text", StringComparison.Ordinal))
                {
                    // Monta a linha completa (caso seja multi‐linha)
                    var full = new StringBuilder(rawLine);
                    if (!t.Contains("</text>"))
                    {
                        int j = i + 1;
                        while (j < lines.Length && !lines[j].Trim().Contains("</text>"))
                        {
                            full.Append("\n").Append(lines[j]);
                            j++;
                        }
                        if (j < lines.Length)
                        {
                            full.Append("\n").Append(lines[j]);
                            i = j; // avança o loop
                        }
                    }

                    var rawText = full.ToString();
                    var mText = rxText.Match(rawText);
                    if (mText.Success)
                    {
                        // 3.3.1) Captura o ID
                        string idAtual = mText.Groups[1].Value;
                        string content = mText.Groups[2].Value;

                        // 3.3.2) Verifica se este ID está em highlightIds
                        bool mustHighlight = highlightIds.Contains(idAtual);

                        // 3.3.3) Se for para destacar, abre \highlight12
                        if (mustHighlight)
                            sb.Append(@"\highlight12 ");

                        // 3.3.4) Começa a tag: <text id="ID">
                        sb.Append(@"\cf2 <text \cf3 id\cf1=\cf4 """)
                          .Append(EscapeRtf(idAtual))
                          .Append(@"""\cf2>\cf1 ");

                        // 3.3.5) Corpo do <text>
                        if (content.Equals("%null%", StringComparison.OrdinalIgnoreCase))
                        {
                            sb.Append(@"\cf5 ").Append(EscapeRtf(content)).Append(@"\cf1 ");
                        }
                        else
                        {
                            int last = 0;
                            foreach (Match tag in rxInlineTag.Matches(content))
                            {
                                // Renderiza o texto antes da tag inline, se houver
                                if (tag.Index > last)
                                    sb.Append(EscapeRtf(content.Substring(last, tag.Index - last)));

                                // Renderiza a tag inline com as cores apropriadas
                                sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[1].Value)); // <? em roxo
                                var core = tag.Groups[2].Value;
                                int at = core.IndexOf('@');
                                if (at >= 0)
                                {
                                    sb.Append(@"\cf8 ").Append(EscapeRtf(core.Substring(0, at))); // Antes do '@'
                                    sb.Append(@"\cf9 ").Append(EscapeRtf(core.Substring(at)));    // Depois do '@'
                                }
                                else
                                {
                                    sb.Append(@"\cf8 ").Append(EscapeRtf(core)); // Sem '@', tudo em uma cor só
                                }
                                sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[3].Value)).Append(@"\cf1 "); // ?> em roxo
                                last = tag.Index + tag.Length;
                            }
                            // Renderiza o resto do texto após a última tag inline, se houver
                            if (last < content.Length)
                                sb.Append(EscapeRtf(content.Substring(last)));
                        }

                        // 3.3.6) Fecha a tag </text> e, se estava destacando, volta o highlight para 0
                        sb.Append(@"\cf2 </text>");
                        if (mustHighlight)
                            sb.Append(@"\highlight0");

                        sb.Append(@"\cf1\line");
                        continue;
                    }
                }

                // 3.4) Fallback para qualquer outra linha
                sb.Append(@"\cf1 ").Append(EscapeRtf(rawLine)).Append(@"\cf1\line");
            }

            // 4) Fecha o RTF
            sb.Append("}");
            return sb.ToString();
        }

        private string BuildRtf(string path)
        {
            return BuildRtf(path, new HashSet<string>());
        }

        private static string EscapeRtf(string s) =>
            s.Replace(@"\", @"\\")
             .Replace("{", @"\{")
             .Replace("}", @"\}")
             .Replace("\n", @"\line ");

        private void FastPopulateByRtf(RichTextBox box, string rtf)
        {
            if (box.InvokeRequired)
                box.Invoke(new Action(() => FastPopulateByRtf(box, rtf)));
            else
            {
                box.BeginUpdate();
                box.SuspendLayout();
                box.Rtf = rtf;
                box.ResumeLayout();
                box.EndUpdate();
            }
        }

        private List<(string Id, string Value, int Line)> LoadXmlList(string path)
        {
            try
            {
                //var xmlContent = File.ReadAllText(path);
                var xmlContent = File.ReadAllText(path, Encoding.UTF8);
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(xmlContent);
                var lines = xmlContent.Replace("\r\n", "\n").Split('\n');
                var result = new List<(string, string, int)>();
                int currentLine = 1;

                var textNodes = xmlDoc.SelectNodes("//text");
                if (textNodes == null)
                {
                    Console.WriteLine("Nenhum nó <text> encontrado no XML.");
                    return result;
                }

                Console.WriteLine($"Total de nós <text> encontrados: {textNodes.Count}");
                foreach (XmlNode node in textNodes)
                {
                    var id = node.Attributes["id"]?.Value;
                    if (string.IsNullOrEmpty(id))
                    {
                        Console.WriteLine($"Nó <text> sem ID na linha {currentLine}");
                        continue;
                    }
                    var inner = node.InnerXml.Trim();

                    for (int i = currentLine; i <= lines.Length; i++)
                    {
                        if (lines[i - 1].Contains($"id=\"{id}\""))
                        {
                            result.Add((id, inner, i));
                            currentLine = i + 1;
                            break;
                        }
                    }
                }

                Console.WriteLine($"Total de entradas processadas: {result.Count}");
                return result;
            }
            catch (XmlException ex)
            {
                Console.WriteLine($"Erro ao parsear XML: {ex.Message}");
                MessageBox.Show($"Erro ao processar o XML: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return new List<(string, string, int)>();
            }
        }

        private async Task LoadFirstFile(string path)
        {
            // 1) executa só o parsing em background
            firstList = await Task.Run(() => LoadXmlList(path));
            if (firstList == null || firstList.Count == 0)
            {
                MessageBox.Show("Nenhum dado válido encontrado no arquivo primário.", "Aviso",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            firstDict = firstList.ToDictionary(x => x.Id, x => x.Value);

            // 2) monta o RTF em background
            string rtf = await Task.Run(() => BuildRtf(path));

            // 3) aplica na UI de uma só vez
            FastPopulateByRtf(txtFirst, rtf);
        }

        private async Task LoadSecondFile(string path)
        {
            secondList = await Task.Run(() => LoadXmlList(path));
            if (secondList == null || secondList.Count == 0)
            {
                MessageBox.Show("Nenhum dado válido encontrado no arquivo secundário.", "Aviso",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            secondDict = secondList.ToDictionary(x => x.Id, x => x.Value);

            string rtf = await Task.Run(() => BuildRtf(path));
            FastPopulateByRtf(txtSecond, rtf);
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private async void btnTranslate_Click(object sender, EventArgs e)
        {
            // Regex que captura blocos <text id="123">conteúdo que pode ter '\n'</text>
            //   - (?<id>[^""]+)  captura o id
            //   - (?<conteudo>[\s\S]*?) captura todo o conteúdo interno, incluindo quebras
            var rxTextBlock = new Regex(@"<text\s+id=""(?<id>[^""]+)""\s*>(?<conteudo>[\s\S]*?)</text>",RegexOptions.Compiled | RegexOptions.Multiline);

            string original = txtComparison.Text;
            var matches = rxTextBlock.Matches(original);
            if (matches.Count == 0)
            {
                MessageBox.Show("Nenhum bloco <text> válido encontrado para traduzir.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Colete id → conteúdo (sem %null%) para traduzir
            var listaParaTraduzir = new List<(string id, string conteudo)>();
            foreach (Match m in matches)
            {
                string id = m.Groups["id"].Value;
                string conteudo = m.Groups["conteudo"].Value;
                if (conteudo.Equals("%null%", StringComparison.OrdinalIgnoreCase))
                    continue;

                listaParaTraduzir.Add((id, conteudo));
            }

            if (listaParaTraduzir.Count == 0)
            {
                MessageBox.Show("Não há textos (diferentes de %null%) para traduzir.", "Aviso",MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Traduzir em lotes, exatamente como antes
            const int batchSize = 5;
            const int delayMs = 1000;
            var mapaTraduzido = new Dictionary<string, string>(); // id -> texto traduzido

            for (int i = 0; i < listaParaTraduzir.Count; i += batchSize)
            {
                var lote = listaParaTraduzir.Skip(i).Take(batchSize).ToList();
                var tarefas = lote
                    .Select(item => TranslateAndReturnPairAsync(item.id, item.conteudo, "en", "pt"))
                    .ToList();

                try
                {
                    var resultados = await Task.WhenAll(tarefas);
                    foreach (var kv in resultados)
                        mapaTraduzido[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao traduzir lote a partir do item {i + 1}: {ex.Message}","Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (i + batchSize < listaParaTraduzir.Count)
                    await Task.Delay(delayMs);
            }

            // Agora fazemos um Replace inteligente em todo o texto, usando Regex.Replace + MatchEvaluator.
            // Para cada bloco <text id="X">…</text>, se 'X' existir em 'mapaTraduzido', substituímos o conteúdo inteiro.
            string novoContent = rxTextBlock.Replace(original, match =>
            {
                string id = match.Groups["id"].Value;
                string conteudoOriginal = match.Groups["conteudo"].Value;

                if (!mapaTraduzido.ContainsKey(id))
                {
                    // Não foi traduzido (talvez era %null%), então devolvemos o bloco original
                    return match.Value;
                }

                string traduzidoBruto = mapaTraduzido[id];

                // Se vier algo já no formato de entidade HTML (< e > escapados), pulamos o EscapeForXml:
                string traducaoFinal;
                if (Regex.IsMatch(traduzidoBruto, @"^&lt;.*\?&gt;$"))
                {
                    traducaoFinal = traduzidoBruto;
                }
                else
                {
                    traducaoFinal = EscapeForXml(traduzidoBruto);
                }

                // Reconstruímos exatamente o mesmo bloco, mas com o conteúdo trocado pela tradução:
                return $"<text id=\"{id}\">{traducaoFinal}</text>";
            });

            // Atribuímos de volta em txtComparison:
            txtComparison.Text = novoContent;

            // Se você quiser manter a coloração RTF no próprio txtComparison (com highlight verde), reaplique aqui:
            try
            {
                // Geramos um arquivo temporário contendo o XML recém‐atualizado
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
                File.WriteAllText(temp, txtComparison.Text, Encoding.UTF8);

                // Conjunto de IDs que precisamos destacar (fundo verde)
                var highlightIds = new HashSet<string>(mapaTraduzido.Keys);

                // BuildRtf monta o RTF colorido com highlight nas linhas traduzidas
                string novoRtf = BuildRtf(temp, highlightIds);

                FastPopulateByRtf(txtComparison, novoRtf);
                File.Delete(temp);
            }
            catch
            {
                // Se o RTF falhar, não impede que o texto já tenha sido convertido no textbox
            }

            MessageBox.Show("Tradução aplicada com sucesso.", "Sucesso", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task<KeyValuePair<string, string>> TranslateAndReturnPairAsync(string id, string texto, string sourceLang, string targetLang)
        {
            string traduzido = await TranslateTextAsync(texto, sourceLang, targetLang);
            return new KeyValuePair<string, string>(id, traduzido);
        }

        private async Task<string> TranslateTextAsync(string texto, string sourceLang, string targetLang)
        {
            // Monta a URL do MyMemory (gratuito, mas limitado). Você pode trocar para outro serviço de tradução em lote.
            string uri = $"https://api.mymemory.translated.net/get?q={Uri.EscapeDataString(texto)}&langpair={sourceLang}|{targetLang}";

            using (var response = await _httpClient.GetAsync(uri))
            {
                response.EnsureSuccessStatusCode();
                string jsonString = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(jsonString);
                string translated = json["responseData"]?["translatedText"]?.ToString() ?? texto;
                return translated;
            }
        }

        private string EscapeForXml(string s)
        {
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }

        private async void TxtFirst_TextChanged(object sender, EventArgs e)
        {
            if (firstList == null && txtFirst.Text.TrimStart().StartsWith("<"))
            {
                var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
                File.WriteAllText(tmp, txtFirst.Text);
                await LoadFirstFile(tmp);
                File.Delete(tmp);
            }
        }

        private async void TxtSecond_TextChanged(object sender, EventArgs e)
        {
            if (secondList == null && txtSecond.Text.TrimStart().StartsWith("<"))
            {
                var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
                File.WriteAllText(tmp, txtSecond.Text);
                await LoadSecondFile(tmp);
                File.Delete(tmp);
            }
        }

        private async void TxtComparison_TextChanged(object sender, EventArgs e)
        {
            //UpdateGutter(txtComparison, txtComparisonNumber);
        }

        private void RichTextBox_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        private async void TxtFirst_DragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && Path.GetExtension(files[0]).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                await LoadFirstFile(files[0]);
        }

        private async void TxtSecond_DragDrop(object sender, DragEventArgs e)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0 && Path.GetExtension(files[0]).Equals(".xml", StringComparison.OrdinalIgnoreCase))
                await LoadSecondFile(files[0]);
        }

        private async void btnLanguageFirst_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "XML Files|*.xml" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    await LoadFirstFile(dlg.FileName);
                }
            }
        }

        private async void btnLanguageSecond_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "XML Files|*.xml" })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    await LoadSecondFile(dlg.FileName);
                }
            }
        }

        private void btnCompare_Click(object sender, EventArgs e)
        {
            var sb = new StringBuilder();
            var gutterNumbers = new List<string>();

            // ─── Cabeçalho RTF e tabela de cores ───
            sb.Append(@"{\rtf1\ansi\deff0");
            sb.Append(@"{\colortbl;");
            sb.Append(@"\red255\green255\blue255;"); // 1 = white
            sb.Append(@"\red255\green121\blue198;"); // 2 = rose
            sb.Append(@"\red69\green250\blue93;");   // 3 = green
            sb.Append(@"\red241\green236\blue94;");  // 4 = yellow
            sb.Append(@"\red150\green150\blue150;"); // 5 = dim (%null%)
            sb.Append(@"\red128\green128\blue128;"); // 6 = gray (números da linha E AGORA O CORE!)
            sb.Append(@"\red189\green147\blue249;"); // 7 = prefixo/sufixo (<? … ?>)
            sb.Append(@"\red255\green190\blue241;"); // 8 = antes do '@'
            sb.Append(@"\red243\green214\blue255;"); // 9 = depois do '@'
            sb.Append(@"\red255\green255\blue0;");   // 10 = yellow (header primário)
            sb.Append(@"\red0\green255\blue255;");   // 11 = cyan (header secundário)
            sb.Append(@"\red0\green100\blue0;");     // 12 = green (fundo destacado)
            sb.Append("}");
            sb.Append(@"\cf1 ");

            // Regex para tags inline &lt;? … ?&gt;
            var rxInlineTag = new Regex(@"(&lt;\?)(.*?)(\?\&gt;)", RegexOptions.Compiled);

            // ─── Idioma primário ───
            sb.Append(@"\cf10 Idioma primário: Não tem no idioma secundário ou era %null%\cf1\line");
            gutterNumbers.Add("");

            foreach (var kv in firstList)
            {
                bool existsInSecond = secondDict.TryGetValue(kv.Id, out var secondVal);
                bool wasNullInSecond = existsInSecond && secondVal.Equals("%null%", StringComparison.OrdinalIgnoreCase);
                bool isNullInFirst = kv.Value.Equals("%null%", StringComparison.OrdinalIgnoreCase);

                if (!existsInSecond || (wasNullInSecond && !isNullInFirst))
                {
                    // Abertura da tag <text id="...">
                    sb.Append(@"\cf2 <text \cf3 id\cf1=\cf4 """)
                      .Append(EscapeRtf(kv.Id))
                      .Append(@"""\cf2>\cf1 ");

                    // ■■■ Bloco “era nulo” ou “foi nulo” ■■■
                    if (isNullInFirst || wasNullInSecond)
                    {
                        // Se for exatamente algo como &lt;?sysmsg@XXXXX?&gt;
                        if (kv.Value.StartsWith("&lt;?") && kv.Value.EndsWith("?&gt;"))
                        {
                            // extrai prefixo, core e sufixo via regex
                            var m = rxInlineTag.Match(kv.Value);
                            if (m.Success)
                            {
                                // prefixo “&lt;?”
                                sb.Append(@"\cf7 ").Append(EscapeRtf(m.Groups[1].Value));
                                // core “sysmsg@XXXXX” em cinza (\cf6)
                                sb.Append(@"\cf6 ").Append(EscapeRtf(m.Groups[2].Value));
                                // sufixo “?&gt;”
                                sb.Append(@"\cf7 ").Append(EscapeRtf(m.Groups[3].Value)).Append(@"\cf1 ");
                            }
                            else
                            {
                                // fallback: pinta tudo em dim (\cf5)
                                sb.Append(@"\cf5 ").Append(EscapeRtf(kv.Value)).Append(@"\cf1 ");
                            }
                        }
                        else
                        {
                            // valor normal que era %null%: pinta tudo em dim (\cf5)
                            sb.Append(@"\cf5 ").Append(EscapeRtf(kv.Value)).Append(@"\cf1 ");
                        }
                    }
                    // ■■■ Bloco “contém tag que não era nula” ■■■
                    else if (kv.Value.StartsWith("&lt;?") && kv.Value.EndsWith("?&gt;"))
                    {
                        int last = 0;
                        foreach (Match tag in rxInlineTag.Matches(kv.Value))
                        {
                            // texto antes da tag normal
                            if (tag.Index > last)
                                sb.Append(EscapeRtf(kv.Value.Substring(last, tag.Index - last)));

                            // prefixo “&lt;?”
                            sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[1].Value));

                            // parte antes/depois do ‘@’
                            var core = tag.Groups[2].Value;
                            int at = core.IndexOf('@');
                            if (at >= 0)
                            {
                                sb.Append(@"\cf8 ").Append(EscapeRtf(core.Substring(0, at)));
                                sb.Append(@"\cf9 ").Append(EscapeRtf(core.Substring(at)));
                            }
                            else
                            {
                                sb.Append(@"\cf8 ").Append(EscapeRtf(core));
                            }

                            // sufixo “?&gt;”
                            sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[3].Value)).Append(@"\cf1 ");
                            last = tag.Index + tag.Length;
                        }

                        // resto depois da última tag
                        if (last < kv.Value.Length)
                            sb.Append(EscapeRtf(kv.Value.Substring(last)));
                    }
                    // ■■■ Bloco “texto comum” ■■■
                    else
                    {
                        sb.Append(EscapeRtf(kv.Value));
                    }

                    // Fechamento </text>\line
                    sb.Append(@"\cf2 </text>\cf1\line");

                    // Adiciona ao gutter o número de linha (considerando linhas com quebras)
                    int wraps = kv.Value.Count(c => c == '\n') + 1;
                    for (int i = 0; i < wraps; i++)
                        gutterNumbers.Add((kv.Line + i).ToString());
                }
            }

            // Espaço entre seções
            sb.Append(@"\line");
            gutterNumbers.Add("");

            // ─── Idioma secundário ───
            sb.Append(@"\cf11 Idioma secundário: Não tem no idioma primário ou era %null%:\cf1\line");
            gutterNumbers.Add("");

            foreach (var kv in secondList)
            {
                bool existsInFirst = firstDict.TryGetValue(kv.Id, out var firstVal);
                bool wasNullInFirst = existsInFirst && firstVal.Equals("%null%", StringComparison.OrdinalIgnoreCase);
                bool isNullInSecond = kv.Value.Equals("%null%", StringComparison.OrdinalIgnoreCase);

                if (!existsInFirst || (wasNullInFirst && !isNullInSecond))
                {
                    // Abertura da tag <text id="...">
                    sb.Append(@"\cf2 <text \cf3 id\cf1=\cf4 """)
                      .Append(EscapeRtf(kv.Id))
                      .Append(@"""\cf2>\cf1 ");

                    // ■■■ Bloco “era nulo” ou “foi nulo” ■■■
                    if (isNullInSecond || wasNullInFirst)
                    {
                        if (kv.Value.StartsWith("&lt;?") && kv.Value.EndsWith("?&gt;"))
                        {
                            var m = rxInlineTag.Match(kv.Value);
                            if (m.Success)
                            {
                                // prefixo “&lt;?” em roxo
                                sb.Append(@"\cf7 ").Append(EscapeRtf(m.Groups[1].Value));
                                // core (sysmsg@XXXXX) em CINZA (\cf6)
                                sb.Append(@"\cf6 ").Append(EscapeRtf(m.Groups[2].Value));
                                // sufixo “?&gt;” em roxo
                                sb.Append(@"\cf7 ").Append(EscapeRtf(m.Groups[3].Value)).Append(@"\cf1 ");
                            }
                            else
                            {
                                sb.Append(@"\cf5 ").Append(EscapeRtf(kv.Value)).Append(@"\cf1 ");
                            }
                        }
                        else
                        {
                            sb.Append(@"\cf5 ").Append(EscapeRtf(kv.Value)).Append(@"\cf1 ");
                        }
                    }
                    // ■■■ Bloco “contém tag que não era nula” ■■■
                    else if (kv.Value.StartsWith("&lt;?") && kv.Value.EndsWith("?&gt;"))
                    {
                        int last = 0;
                        foreach (Match tag in rxInlineTag.Matches(kv.Value))
                        {
                            if (tag.Index > last)
                                sb.Append(EscapeRtf(kv.Value.Substring(last, tag.Index - last)));

                            sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[1].Value));

                            var core = tag.Groups[2].Value;
                            int at = core.IndexOf('@');
                            if (at >= 0)
                            {
                                sb.Append(@"\cf8 ").Append(EscapeRtf(core.Substring(0, at)));
                                sb.Append(@"\cf9 ").Append(EscapeRtf(core.Substring(at)));
                            }
                            else
                            {
                                sb.Append(@"\cf8 ").Append(EscapeRtf(core));
                            }

                            sb.Append(@"\cf7 ").Append(EscapeRtf(tag.Groups[3].Value)).Append(@"\cf1 ");
                            last = tag.Index + tag.Length;
                        }

                        if (last < kv.Value.Length)
                            sb.Append(EscapeRtf(kv.Value.Substring(last)));
                    }
                    // ■■■ Bloco “texto comum” ■■■
                    else
                    {
                        sb.Append(EscapeRtf(kv.Value));
                    }

                    // Fechamento </text>\line
                    sb.Append(@"\cf2 </text>\cf1\line");

                    int wraps = kv.Value.Count(c => c == '\n') + 1;
                    for (int i = 0; i < wraps; i++)
                        gutterNumbers.Add((kv.Line + i).ToString());
                }
            }

            sb.Append("}");

            // Se não encontrar <text em “diferenças simples”
            if (!sb.ToString().Contains("<text"))
            {
                MessageBox.Show("Nenhuma diferença simples encontrada.", "Resultado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            FastPopulateByRtf(txtComparison, sb.ToString());
            txtComparisonNumber.Lines = gutterNumbers.ToArray();
            SyncScroll(txtComparison, txtComparisonNumber);
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            // 1) Regex para capturar linhas do tipo:
            //    [indent opcional]<text id="123">conteúdo</text>
            var rxTextLine = new Regex(@"^(?<indent>\s*)<text\s+id=""(?<id>\d+)""\s*>(?<content>.*?)</text>\s*$");

            // 2) Separa txtFirst e txtComparison (não txtSecond) em listas de linhas, preservando quebras vazias
            var firstLines = txtFirst.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var comparisonLines = txtComparison.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // 3) Reconstrói o dicionário (id → conteúdo exato) a partir de txtComparison
            //    Isso garante que, se você alterou “Teste de aptidão” para “Teste de aptidão 222”
            //    no txtComparison, será esse valor atualizado que entrará em txtFirst agora.
            var segundaChaveValor = new Dictionary<string, string>();
            foreach (var raw in comparisonLines)
            {
                var m2 = rxTextLine.Match(raw);
                if (!m2.Success)
                    continue;

                string idComp = m2.Groups["id"].Value;      // ex: "113"
                string conteudoComp = m2.Groups["content"].Value; // ex: "Teste de aptidão 222" (pode ter sido editado)

                // Se não for "%null%", usamos exatamente o texto que está em comparison
                if (!conteudoComp.Equals("%null%", StringComparison.OrdinalIgnoreCase))
                    segundaChaveValor[idComp] = conteudoComp;
            }

            // 4) Monta o conjunto de IDs que devem ficar com fundo verde (highlight) nesta execução.
            //    Basta usar todas as chaves de 'segundaChaveValor', pois são justamente os IDs
            //    que têm algum valor “real” em comparação (diferente de %null%).
            var highlightIds = new HashSet<string>(segundaChaveValor.Keys);

            // 5) Descobre a “indentação padrão” que as linhas existentes em txtFirst usam,
            //    para alinhar corretamente qualquer linha nova ou substituída.
            string indentPadrao = "";
            {
                int idxOpen = firstLines.FindIndex(l => l.Trim().StartsWith("<entries>"));
                if (idxOpen >= 0)
                {
                    for (int k = idxOpen + 1; k < firstLines.Count; k++)
                    {
                        var mm = rxTextLine.Match(firstLines[k]);
                        if (mm.Success)
                        {
                            // Captura todos os espaços ou tabs antes de "<text"
                            indentPadrao = mm.Groups["indent"].Value;
                            break;
                        }
                        if (firstLines[k].Trim().StartsWith("</entries>"))
                        {
                            // Se não achar nenhum <text> antes de </entries>, usa 2 espaços como fallback
                            indentPadrao = "  ";
                            break;
                        }
                    }
                }
            }

            // 6) Substitui em firstLines toda linha "<text id="X">...</text>" cujo X esteja em segundaChaveValor
            for (int i = 0; i < firstLines.Count; i++)
            {
                var linha = firstLines[i];
                var m1 = rxTextLine.Match(linha);
                if (!m1.Success)
                    continue;

                string id1 = m1.Groups["id"].Value;      // ex: "113"
                                                         //string conteudo1 = m1.Groups["content"].Value; // Não precisamos mais verificar se é %null%

                // Se o ID existe em segundaChaveValor, substituímos o conteúdo pela string exata vinda do txtComparison.
                if (segundaChaveValor.ContainsKey(id1))
                {
                    string novoCont = segundaChaveValor[id1];
                    firstLines[i] = $"{indentPadrao}<text id=\"{id1}\">{novoCont}</text>";
                }
            }

            // 7) Coleta todos os IDs que já existem (após substituição) em firstLines
            var idsExistentesNoFirst = new HashSet<string>();
            foreach (var linha in firstLines)
            {
                var mm = rxTextLine.Match(linha);
                if (mm.Success)
                    idsExistentesNoFirst.Add(mm.Groups["id"].Value);
            }

            // 8) Encontra índices de <entries> e </entries> em firstLines
            int idxEntriesOpen = firstLines.FindIndex(l => l.Trim().StartsWith("<entries>"));
            int idxEntriesClose = firstLines.FindIndex(l => l.Trim().Equals("</entries>"));
            if (idxEntriesOpen < 0 || idxEntriesClose < 0 || idxEntriesClose < idxEntriesOpen)
            {
                MessageBox.Show("Não foi possível localizar as tags <entries> em txtFirst.","Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 9) Identifica quais IDs em segundaChaveValor NÃO existem ainda em firstLines → vamos inserir
            var paraInserir = segundaChaveValor
                .Where(kv => !idsExistentesNoFirst.Contains(kv.Key))
                .Select(kv => new { Id = kv.Key, Conteudo = kv.Value })
                .ToList();

            if (paraInserir.Count > 0)
            {
                // 10) Divide a lista em IDs numéricos (para ordenar numericamente) e não numéricos
                var listaNumerica = new List<(int idNum, string idStr, string conteudo)>();
                var listaAlfaNum = new List<(string idStr, string conteudo)>();

                foreach (var item in paraInserir)
                {
                    if (int.TryParse(item.Id, out int idNum))
                        listaNumerica.Add((idNum, item.Id, item.Conteudo));
                    else
                        listaAlfaNum.Add((item.Id, item.Conteudo));
                }

                // 11) Ordena IDs numéricos de forma crescente
                listaNumerica.Sort((a, b) => a.idNum.CompareTo(b.idNum));

                // 12) Insere cada uma destas linhas de trás para frente dentro de <entries>…</entries>
                foreach (var item in listaNumerica.AsEnumerable().Reverse())
                {
                    bool inserido = false;
                    // Exemplo de linha:   "  <text id="50520">Test of Aptitude 222</text>"
                    string novaLinha = $"{indentPadrao}<text id=\"{item.idStr}\">{item.conteudo}</text>";

                    for (int j = idxEntriesOpen + 1; j < idxEntriesClose; j++)
                    {
                        var mm = rxTextLine.Match(firstLines[j]);
                        if (mm.Success && int.TryParse(mm.Groups["id"].Value, out int idExistente))
                        {
                            if (item.idNum < idExistente)
                            {
                                firstLines.Insert(j, novaLinha);
                                inserido = true;
                                break;
                            }
                        }
                    }
                    if (!inserido)
                    {
                        // Se não encontrou ID maior, insere logo antes de </entries>
                        firstLines.Insert(idxEntriesClose, novaLinha);
                        idxEntriesClose++;
                    }
                }

                // 13) IDs não numéricos (caso existam) entram no final de <entries>
                foreach (var item in listaAlfaNum)
                {
                    string novaLinha = $"{indentPadrao}<text id=\"{item.idStr}\">{item.conteudo}</text>";
                    firstLines.Insert(idxEntriesClose, novaLinha);
                    idxEntriesClose++;
                }
            }

            // 14) Atualiza txtFirst.Text com o XML puro atualizado (substituições + inserções)
            txtFirst.Text = string.Join(Environment.NewLine, firstLines);

            // 15) Reaplica o RTF colorido e pinta de verde TODOS os IDs de highlightIds,
            //     garantindo que o fundo verde não desapareça ao clicar novamente.
            try
            {
                // Grava temporariamente o arquivo para o BuildRtf
                var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xml");
                File.WriteAllText(temp, txtFirst.Text, Encoding.UTF8);

                // Chama BuildRtf passando highlightIds (todos os IDs que vieram de txtComparison)
                string novoRtf = BuildRtf(temp, highlightIds);

                FastPopulateByRtf(txtFirst, novoRtf);
                UpdateGutter(txtFirst, txtFirstNumber);

                File.Delete(temp);
            }
            catch
            {
                // Se algo der errado, exiba ou ignore
                // MessageBox.Show("Não foi possível recolorir RTF.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

    }
}