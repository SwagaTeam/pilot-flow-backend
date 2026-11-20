using System.Globalization;
using System.Text.Json;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PilotFlow.Application.Decisions;
using PilotFlow.Application.Evaluation;
using PilotFlow.Infrastructure.Persistence.Decisions;

namespace PilotFlow.Infrastructure.Documents;

internal sealed class PdfDecisionRenderer : IDecisionPdfRenderer
{
    private const string FontName = "Pilot Flow Sans";
    private static readonly Color Ink = Color.FromRgb(29, 43, 61);
    private static readonly Color Accent = Color.FromRgb(25, 89, 92);
    private static readonly Color Paper = Color.FromRgb(235, 244, 243);

    static PdfDecisionRenderer() => GlobalFontSettings.FontResolver = new DocumentFontResolver();

    public byte[] Render(JsonElement snapshot, DecisionDocument document, string? watermark)
    {
        var data = SnapshotJson.Deserialize(snapshot);
        var output = new Document();
        output.Info.Title = "Выбор пилотного потока";
        output.Info.Subject = data.Version.Data.Title;
        var normal = output.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = 6;
        var heading = output.Styles[StyleNames.Heading1]!;
        heading.Font.Name = FontName;
        heading.Font.Size = 20;
        heading.Font.Bold = true;
        heading.Font.Color = Ink;
        heading.ParagraphFormat.SpaceAfter = 16;
        var subheading = output.Styles[StyleNames.Heading2]!;
        subheading.Font.Name = FontName;
        subheading.Font.Size = 12;
        subheading.Font.Bold = true;
        subheading.Font.Color = Accent;
        subheading.ParagraphFormat.SpaceBefore = 11;
        subheading.ParagraphFormat.SpaceAfter = 6;

        if (document is DecisionDocument.Bundle or DecisionDocument.Protocol)
        {
            WriteProtocol(CreateSection(output, data, landscape: false), data);
        }
        if (document is DecisionDocument.Bundle or DecisionDocument.FamilyMatrix)
        {
            WriteFamilyMatrix(CreateSection(output, data, landscape: true), data);
        }
        if (document is DecisionDocument.Bundle or DecisionDocument.SelectionMatrix)
        {
            WriteSelectionMatrix(CreateSection(output, data, landscape: true), data);
        }

        var renderer = new PdfDocumentRenderer { Document = output };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.Info.CreationDate = data.Version.UpdatedAt.UtcDateTime;
        pdf.Info.ModificationDate = data.Version.UpdatedAt.UtcDateTime;
        if (watermark is not null)
        {
            var font = new XFont(FontName, 56, XFontStyleEx.Bold);
            var brush = new XSolidBrush(XColor.FromArgb(35, 25, 89, 92));
            foreach (var page in pdf.Pages)
            {
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var center = new XPoint(page.Width.Point / 2, page.Height.Point / 2);
                graphics.RotateAtTransform(-31, center);
                graphics.DrawString(watermark, font, brush, center, XStringFormats.Center);
            }
        }

        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static Section CreateSection(Document document, VersionSnapshot snapshot, bool landscape)
    {
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.1);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.0);
        var header = section.Headers.Primary.AddParagraph();
        header.AddText("ПИЛОТНЫЙ ПОТОК");
        header.Format.Font.Color = Accent;
        header.Format.Font.Bold = true;
        header.Format.Font.Size = 8;
        header.Format.Borders.Bottom.Width = 0.6;
        header.Format.Borders.Bottom.Color = Accent;
        header.Format.Borders.DistanceFromBottom = 4;
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText(FormattableString.Invariant($"Версия {snapshot.Version.VersionNumber} · Ревизия {snapshot.Version.Revision} · Стр. "));
        footer.AddPageField();
        return section;
    }

    private static void WriteProtocol(Section section, VersionSnapshot snapshot)
    {
        section.AddParagraph("Протокол выбора пилотного потока", StyleNames.Heading1);
        var protocol = snapshot.Protocol;
        Field(section, "Протокол", protocol.ProtocolNumber);
        Field(section, "Дата протокола", Date(protocol.ProtocolDate));
        Field(section, "Место подписания", protocol.SigningPlace);
        Field(section, "Соглашение", Join(protocol.AgreementNumber, Date(protocol.AgreementDate)));
        Field(section, "Предприятие", protocol.EnterpriseParty ?? snapshot.Version.Data.EnterpriseName);
        Field(section, "Партнёр", protocol.PartnerParty);

        section.AddParagraph("Решение", StyleNames.Heading2);
        if (snapshot.Selection is { } selection)
        {
            var family = snapshot.Families.Single(item => item.Id == selection.FamilyId);
            var evaluation = snapshot.Evaluation.Families.FirstOrDefault(item => item.FamilyId == family.Id);
            Field(section, "Выбранный поток", family.Data.Name);
            Field(section, "Границы потока", Join(family.Data.StartBoundary, family.Data.EndBoundary, " — "));
            Field(section, "Способ выбора", selection.Mode switch
            {
                "ranked" => "По рейтингу",
                "monoproduct" => "Монопродуктовое предприятие",
                "dominantProduct" => "Доминирующий продукт",
                _ => selection.Mode,
            });
            if (selection.DominantProductId is { } productId)
            {
                Field(section, "Доминирующий продукт", snapshot.Products.FirstOrDefault(item => item.Id == productId)?.Data.Name);
            }
            if (selection.Mode == "ranked")
            {
                Field(section, "Баллы и место", Join(evaluation?.TotalScore, evaluation?.Rank?.ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                Field(section, "Источник прямого выбора", selection.DirectBasisEvidence.Source);
                Field(section, "Основание прямого выбора", selection.DirectBasisEvidence.Basis);
            }
            Field(section, "Обоснование", selection.Justification);
            Field(section, "Решение руководителя", selection.ManagerDecisionReference);
        }

        Field(section, "Выводы", protocol.Conclusions);
        section.AddParagraph("Подготовка и утверждение", StyleNames.Heading2);
        Field(section, "Подготовил", Join(protocol.PreparedBy.FullName, protocol.PreparedBy.Position));
        Field(section, "Утвердил", Join(protocol.ApprovedBy.FullName, protocol.ApprovedBy.Position));
    }

    private static void WriteFamilyMatrix(Section section, VersionSnapshot snapshot)
    {
        section.AddParagraph("Матрица формирования потоков", StyleNames.Heading1);
        Field(section, "Предприятие", snapshot.Version.Data.EnterpriseName);
        var period = snapshot.Version.Data.FinancialPeriod;
        Field(section, "Финансовый период", Join(Date(period?.Start), Date(period?.End), " — "));

        var products = snapshot.Products.ToDictionary(item => item.Id);
        var families = snapshot.Families.ToDictionary(item => item.Id);
        var table = AddTable(section, [5.0, 4.4, 2.5, 2.5, 2.7, 2.4, 2.0, 2.4, 2.1],
            ["Продукция", "Поток", "Выручка, руб.", "Марж. доход, руб.", "Марж. доход на ед., руб.",
                "Доля выручки, %", "Маржа, %", "Маршрут, ч", "Вхождений"]);
        foreach (var row in snapshot.FamilyMatrix.Products.OrderBy(row => products[row.ProductId].Data.Name, StringComparer.Ordinal)
            .ThenBy(row => row.ProductId))
        {
            var product = products[row.ProductId].Data;
            AddRow(table, false, [product.Name, row.FamilyId is { } familyId ? families[familyId].Data.Name : "—",
                Number(product.Revenue), row.MarginIncome, row.MarginIncomePerUnit,
                row.RevenueSharePercent, row.MarginPercent, row.RouteDurationHours,
                row.OperationOccurrenceCount.ToString(CultureInfo.InvariantCulture)]);
        }

        if (table.Rows.Count == 1)
        {
            section.AddParagraph("Продукция отсутствует.");
        }

        section.AddParagraph("Разброс длительности маршрутов", StyleNames.Heading2);
        var cycles = AddTable(section, [8.5, 4.5, 12.3], ["Поток", "Разница, %", "Статус"]);
        foreach (var cycle in snapshot.FamilyMatrix.Cycles.OrderBy(cycle => families[cycle.FamilyId].Data.Name, StringComparer.Ordinal))
        {
            AddRow(cycles, false, [families[cycle.FamilyId].Data.Name, cycle.CycleDifferencePercent, cycle.Status switch
            {
                "withinRecommendation" => "В пределах рекомендации",
                "exceedsRecommendation" => "Превышено рекомендуемое значение",
                _ => "Нет данных",
            }]);
        }
        Field(section, "Выводы", snapshot.Version.Data.FamilyConclusions, afterTable: true);
    }

    private static void WriteSelectionMatrix(Section section, VersionSnapshot snapshot)
    {
        section.AddParagraph("Матрица выбора пилотного потока", StyleNames.Heading1);
        var names = snapshot.Families.ToDictionary(family => family.Id, family => family.Data.Name);
        var table = AddTable(section, [4.4, 2.6, 2.1, 2.2, 2.1, 2.1, 2.3, 2.4, 2.6, 2.5],
            ["Поток", "Допуск", "Доля", "Маржа", "Рост", "Загрузка", "Тип произв.", "Трудоёмк.", "Баллы", "Место"]);
        foreach (var family in snapshot.Evaluation.Families.OrderBy(row => row.Rank ?? int.MaxValue)
            .ThenBy(row => names[row.FamilyId], StringComparer.Ordinal))
        {
            var scores = family.Scores.ToDictionary(score => score.Criterion);
            AddRow(table, false, [names[family.FamilyId], Admission(family), Score("revenueShare"), Score("margin"),
                Score("salesGrowth"), Score("load"), Score("productionType"), Score("labor"),
                family.TotalScore ?? (family.CalculatedCriterionCount == 0 ? null : family.KnownScoreSum + " (часть)"),
                family.Rank?.ToString(CultureInfo.InvariantCulture)]);

            string? Score(string criterion) => scores.GetValueOrDefault(criterion)?.Score;
        }
        if (table.Rows.Count == 1)
        {
            section.AddParagraph("Потоки отсутствуют.");
        }
        if (snapshot.Selection is { } selection)
        {
            Field(section, "Выбранный поток", names.GetValueOrDefault(selection.FamilyId), afterTable: true);
        }
        Field(section, "Выводы", snapshot.Version.Data.RankingConclusions);
    }

    private static string Admission(FamilyEvaluationResult family) => family.Admission switch
    {
        "admitted" => "Допущен",
        "excluded" => "Исключён",
        _ => "Нет данных",
    };

    private static Table AddTable(Section section, double[] widths, string[] labels)
    {
        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(193, 209, 209);
        table.Borders.Width = 0.5;
        table.LeftPadding = Unit.FromMillimeter(2);
        table.RightPadding = Unit.FromMillimeter(2);
        table.TopPadding = Unit.FromMillimeter(1.1);
        table.BottomPadding = Unit.FromMillimeter(1.1);
        foreach (var width in widths)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }
        AddRow(table, true, labels);
        return table;
    }

    private static void AddRow(Table table, bool header, string?[] values)
    {
        var row = table.AddRow();
        row.HeadingFormat = header;
        if (header || table.Rows.Count % 2 == 1)
        {
            row.Shading.Color = Paper;
        }
        for (var index = 0; index < values.Length; index++)
        {
            var paragraph = row.Cells[index].AddParagraph();
            paragraph.Format.Font.Size = 8;
            paragraph.Format.Font.Bold = header;
            paragraph.Format.SpaceAfter = 0;
            paragraph.AddText(values[index] ?? "—");
        }
    }

    private static void Field(Section section, string label, string? value, bool afterTable = false)
    {
        var paragraph = section.AddParagraph();
        if (afterTable)
        {
            paragraph.Format.SpaceBefore = 6;
        }
        paragraph.AddFormattedText(label + "  ", TextFormat.Bold);
        var lines = (string.IsNullOrWhiteSpace(value) ? "—" : value).ReplaceLineEndings("\n").Split('\n');
        paragraph.AddText(lines[0]);
        foreach (var line in lines.Skip(1))
        {
            paragraph.AddLineBreak();
            paragraph.AddText(line);
        }
    }

    private static string? Number(decimal? value) => value?.ToString("0.######", CultureInfo.InvariantCulture);
    private static string? Date(DateOnly? date) => date?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string? Join(string? first, string? second, string separator = " · ") =>
        string.IsNullOrWhiteSpace(first) ? second : string.IsNullOrWhiteSpace(second) ? first : first + separator + second;

    private sealed class DocumentFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(OperatingSystem.IsWindows()
                ? (bold, italic) switch
                {
                    (true, true) => "arialbi.ttf",
                    (true, false) => "arialbd.ttf",
                    (false, true) => "ariali.ttf",
                    _ => "arial.ttf",
                }
                : (bold, italic) switch
                {
                    (true, true) => "DejaVuSans-BoldOblique.ttf",
                    (true, false) => "DejaVuSans-Bold.ttf",
                    (false, true) => "DejaVuSans-Oblique.ttf",
                    _ => "DejaVuSans.ttf",
                });

        public byte[] GetFont(string faceName)
        {
            var directories = OperatingSystem.IsWindows()
                ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.Fonts) }
                : new[] { "/usr/share/fonts/dejavu", "/usr/share/fonts/truetype/dejavu" };
            var path = directories.Select(directory => Path.Combine(directory, faceName)).FirstOrDefault(File.Exists);
            return path is null ? throw new FileNotFoundException("PDF font is missing.", faceName) : File.ReadAllBytes(path);
        }
    }
}
