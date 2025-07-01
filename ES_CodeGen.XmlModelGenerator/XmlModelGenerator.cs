#nullable enable
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Xml.Linq;

namespace ES_CodeGen.XmlModelGenerator
{
    [Generator]
    public class XmlModelGenerator : IIncrementalGenerator
    {
        private const string ConsumerNamespace = "ES_CodeGen.AppConsumer";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            /*#if DEBUG
                if (!System.Diagnostics.Debugger.IsAttached);
                System.Diagnostics.Debugger.Launch();
            #endif*/

            var additionalFiles = context.AdditionalTextsProvider
                .Where(file => file.Path.EndsWith(".model.xml"));

            var xmlFileAndContent = additionalFiles.Select((file, ct) =>
                (file.Path, Content: file.GetText(ct)?.ToString()));

            context.RegisterSourceOutput(xmlFileAndContent, (spc, fileData) =>
            {
                var (path, content) = fileData;

                try
                {
                    if (content != null)
                    {
                        var xml = XDocument.Parse(content);
                        var classElement = xml.Root;
                        var className = classElement?.Attribute("name")?.Value;

                        if (string.IsNullOrWhiteSpace(className)) return;

                        var mappedProperties =
                            new List<(string Name, string? EntityType, string? DtoType, bool InEntity, bool InDTO)>();

                        if (classElement != null && className != null)
                        {
                            var entitySource = GenerateEntity(classElement, className, mappedProperties);
                            var dtoSource = GenerateDtoWithMapping(className, mappedProperties);

                            spc.AddSource($"{className}.Entity.g.cs", SourceText.From(entitySource, Encoding.UTF8));
                            spc.AddSource($"{className}.DTO.g.cs", SourceText.From(dtoSource, Encoding.UTF8));
                        }
                    }
                }
                catch
                {
                    // Ignora arquivos malformados
                }
            });
        }

        private string GenerateEntity(XElement classElement, string className,
            List<(string Name, string? EntityType, string? DtoType, bool InEntity, bool InDTO)> mapped)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine($"namespace {ConsumerNamespace}.Entities;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"public class {className}");
            stringBuilder.AppendLine("{");

            foreach (var prop in classElement.Elements("Property"))
            {
                var (name, type, inEntity, inDto) = ParseProperty(prop);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type)) continue;

                if (inEntity)
                    stringBuilder.AppendLine($"    public {type} {name} {{ get; set; }}");

                var index = mapped.FindIndex(p => p.Name == name);
                if (index >= 0)
                {
                    var existing = mapped[index];
                    mapped[index] = (
                        name,
                        inEntity ? type : existing.EntityType,
                        inDto ? type : existing.DtoType,
                        existing.InEntity || inEntity,
                        existing.InDTO || inDto
                    );
                }
                else
                {
                    mapped.Add((name, inEntity ? type : null, inDto ? type : null, inEntity, inDto));
                }
            }

            stringBuilder.AppendLine("}");
            return stringBuilder.ToString();
        }

        private string GenerateDtoWithMapping(string className,
            List<(string Name, string? EntityType, string? DtoType, bool InEntity, bool InDTO)> mapped)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine($"using {ConsumerNamespace}.Entities;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"namespace {ConsumerNamespace}.DTOs;");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"public class {className}DTO");
            stringBuilder.AppendLine("{");

            foreach (var (name, _, dtoType, _, inDto) in mapped)
            {
                if (inDto && !string.IsNullOrWhiteSpace(dtoType))
                    stringBuilder.AppendLine($"    public {dtoType} {name} {{ get; set; }}");
            }

            stringBuilder.AppendLine("}");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"public static class {className}Mapper");
            stringBuilder.AppendLine("{");

            stringBuilder.AppendLine($"    public static {className}DTO MapToDTO(this {className} entity) => new {className}DTO");
            stringBuilder.AppendLine("    {");

            foreach (var (name, _, dtoType, inEntity, inDto) in mapped)
            {
                if (inEntity && inDto && !string.IsNullOrWhiteSpace(dtoType))
                {
                    if (dtoType != null && !IsPrimitiveOrCollection(dtoType))
                        stringBuilder.AppendLine($"        {name} = entity.{name}?.MapToDTO(),");
                    else
                        stringBuilder.AppendLine($"        {name} = entity.{name},");
                }
            }

            stringBuilder.AppendLine("    };");
            stringBuilder.AppendLine();

            stringBuilder.AppendLine($"    public static {className} MapToEntity(this {className}DTO dto) => new {className}");
            stringBuilder.AppendLine("    {");

            foreach (var (name, entityType, _, inEntity, inDto) in mapped)
            {
                if (inEntity && inDto && !string.IsNullOrWhiteSpace(entityType))
                {
                    if (entityType != null && !IsPrimitiveOrCollection(entityType))
                        stringBuilder.AppendLine($"        {name} = dto.{name}?.MapToEntity(),");
                    else
                        stringBuilder.AppendLine($"        {name} = dto.{name},");
                }
            }

            stringBuilder.AppendLine("    };");
            stringBuilder.AppendLine("}");

            return stringBuilder.ToString();
        }

        private bool IsPrimitiveOrCollection(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return true;

            var primitives = new HashSet<string>
            {
                "int", "string", "bool", "decimal", "double", "float", "long", "short", "byte", "char",
                "uint", "ulong", "ushort", "sbyte", "DateTime", "Guid"
            };

            var cleanType = type.TrimEnd('?', ' ').Split('<')[0];

            if (primitives.Contains(cleanType))
                return true;

            if (type.StartsWith("List<") || type.StartsWith("IEnumerable<") || type.EndsWith("[]"))
                return true;

            return false;
        }

        private (string Name, string Type, bool InEntity, bool InDTO) ParseProperty(XElement prop)
        {
            var name = prop.Attribute("name")?.Value;
            var type = prop.Attribute("type")?.Value;
            var inEntity = bool.TryParse(prop.Attribute("inEntity")?.Value, out var ie) && ie;
            var inDto = bool.TryParse(prop.Attribute("inDTO")?.Value, out var id) && id;
            return (name ?? "", type ?? "", inEntity, inDto);
        }
    }
}