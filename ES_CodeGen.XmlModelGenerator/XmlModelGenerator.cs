using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Xml.Linq;

namespace ES_CodeGen.XmlModelGenerator
{
    [Generator]
    public class XmlModelGenerator : IIncrementalGenerator
    {
        private const string CONSUMER_NAMESPACE = "ES_CodeGen.AppConsumer";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
//#if DEBUG
//            if (!System.Diagnostics.Debugger.IsAttached)
//                System.Diagnostics.Debugger.Launch();
//#endif

            var additionalFiles = context.AdditionalTextsProvider.Where(file => file.Path.EndsWith(".model.xml"));

            var xmlFileAndContent = additionalFiles.Select((file, ct) =>
                (file.Path, Content: file.GetText(ct).ToString()));

            context.RegisterSourceOutput(xmlFileAndContent, (spc, fileData) =>
            {
                var (path, content) = fileData;
                try
                {
                    var xml = XDocument.Parse(content);
                    var classElement = xml.Root;
                    var className = classElement?.Attribute("name")?.Value;

                    if (string.IsNullOrWhiteSpace(className)) return;

                    var entityBuilder = new StringBuilder();
                    var dtoBuilder = new StringBuilder();

                    entityBuilder.AppendLine($"namespace {CONSUMER_NAMESPACE}.Models;");

                    entityBuilder.AppendLine($"public class {className}");
                    entityBuilder.AppendLine("{");

                    dtoBuilder.AppendLine($"namespace {CONSUMER_NAMESPACE}.DTOs;");
                    dtoBuilder.AppendLine($"public class {className}DTO");
                    dtoBuilder.AppendLine("{");

                    foreach (var prop in classElement.Elements("Property"))
                    {
                        var name = prop.Attribute("name")?.Value;
                        var type = prop.Attribute("type")?.Value;
                        var inEntity = bool.TryParse(prop.Attribute("inEntity")?.Value, out var ie) && ie;
                        var inDTO = bool.TryParse(prop.Attribute("inDTO")?.Value, out var id) && id;

                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
                            continue;

                        if (inEntity)
                            entityBuilder.AppendLine($"    public {type} {name} {{ get; set; }}");

                        if (inDTO)
                            dtoBuilder.AppendLine($"    public {type} {name} {{ get; set; }}");
                    }

                    entityBuilder.AppendLine("}");
                    dtoBuilder.AppendLine("}");

                    spc.AddSource($"{className}.Entity.g.cs", SourceText.From(entityBuilder.ToString(), Encoding.UTF8));
                    spc.AddSource($"{className}.DTO.g.cs", SourceText.From(dtoBuilder.ToString(), Encoding.UTF8));
                }
                catch
                {
                    // Ignora arquivos malformados
                }
            });
        }
    }
}