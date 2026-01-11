using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http; // 追加

// コマンドライン引数対応
string openApiJsonPath = args.Length > 0 ? args[0] : "openapi.json"; // OpenAPI定義ファイルのパス
string outputDir = args.Length > 1 ? args[1] : "GeneratedControllers"; // コントローラー出力先ディレクトリ
string dtoDir = args.Length > 2 ? args[2] : "GeneratedDtos"; // DTO出力先ディレクトリ

// 入力ファイル存在チェックとUsage出力
if (!File.Exists(openApiJsonPath))
{
    Console.WriteLine("Usage: OpenApiTools <OpenApiJsonPath> <ControllerOutputDir> <DtoOutputDir>");
    Console.WriteLine("  <OpenApiJsonPath>      OpenAPI定義ファイルのパス（省略時: openapi.json）");
    Console.WriteLine("  <ControllerOutputDir>  コントローラー出力先ディレクトリ（省略時: GeneratedControllers）");
    Console.WriteLine("  <DtoOutputDir>         DTO出力先ディレクトリ（省略時: GeneratedDtos）");
    return;
}

if (!Directory.Exists(outputDir))
{
    Directory.CreateDirectory(outputDir);
}
if (!Directory.Exists(dtoDir))
{
    Directory.CreateDirectory(dtoDir);
}

string json = File.ReadAllText(openApiJsonPath);
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;

Dictionary<string, string> generatedDtos = new();

// パスごとにコントローラーを生成
if (root.TryGetProperty("paths", out var paths))
{
    foreach (var path in paths.EnumerateObject())
    {
        string controllerName = GetControllerName(path.Name);
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
        sb.AppendLine("using Microsoft.AspNetCore.Http;"); // 追加
        sb.AppendLine();
        sb.AppendLine("[ApiController]");
        sb.AppendLine($"[Route(\"api/[controller]\")]");
        sb.AppendLine($"public class {controllerName} : ControllerBase");
        sb.AppendLine("{");

        foreach (var method in path.Value.EnumerateObject())
        {
            string actionName = GetActionName(method.Name, path.Name);
            string httpAttr = method.Name.ToUpper() switch
            {
                "GET" => "[HttpGet]",
                "POST" => "[HttpPost]",
                "PUT" => "[HttpPut]",
                "DELETE" => "[HttpDelete]",
                _ => $"[Http{method.Name.ToUpper()}]"
            };
            sb.AppendLine($"    {httpAttr}");
            sb.AppendLine($"    [Route(\"{path.Name.TrimStart('/')}\")]");

            // パラメータ解析
            var parameters = new List<string>();
            if (method.Value.TryGetProperty("parameters", out var paramArray))
            {
                foreach (var param in paramArray.EnumerateArray())
                {
                    string pname = param.GetProperty("name").GetString()!;
                    string ptype = MapOpenApiType(param.GetProperty("schema").GetProperty("type").GetString()!);
                    string psource = param.GetProperty("in").GetString()!;
                    string attr = psource switch
                    {
                        "query" => "[FromQuery]",
                        "path" => "[FromRoute]",
                        "header" => "[FromHeader]",
                        _ => ""
                    };
                    parameters.Add($"{attr} {ptype} {pname}");
                }
            }

            // リクエストボディ解析（multipart対応拡張）
            string? requestBodyType = null;
            bool isMultipart = false;
            if (method.Value.TryGetProperty("requestBody", out var reqBody))
            {
                if (reqBody.TryGetProperty("content", out var content))
                {
                    // multipart/form-data対応
                    if (content.TryGetProperty("multipart/form-data", out var multipart) &&
                        multipart.TryGetProperty("schema", out var schema))
                    {
                        isMultipart = true;
                        if (schema.TryGetProperty("properties", out var props))
                        {
                            foreach (var prop in props.EnumerateObject())
                            {
                                string pname = prop.Name;
                                var propSchema = prop.Value;
                                if (propSchema.TryGetProperty("type", out var t) &&
                                    t.GetString() == "string" &&
                                    propSchema.TryGetProperty("format", out var f) &&
                                    f.GetString() == "binary")
                                {
                                    // ファイルアップロード
                                    parameters.Add($"[FromForm] IFormFile {pname}");
                                }
                                else if (propSchema.TryGetProperty("type", out var t2) && t2.GetString() == "array" &&
                                         propSchema.TryGetProperty("items", out var items) &&
                                         items.TryGetProperty("type", out var it) && it.GetString() == "string" &&
                                         items.TryGetProperty("format", out var ifmt) && ifmt.GetString() == "binary")
                                {
                                    // 複数ファイル
                                    parameters.Add($"[FromForm] List<IFormFile> {pname}");
                                }
                                else
                                {
                                    // その他のフォームデータ
                                    string ptype = "string";
                                    if (propSchema.TryGetProperty("type", out var t3))
                                        ptype = MapOpenApiType(t3.GetString()!);
                                    parameters.Add($"[FromForm] {ptype} {pname}");
                                }
                            }
                        }
                    }
                    // application/jsonの場合は従来通り
                    else if (content.TryGetProperty("application/json", out var appJson) &&
                             appJson.TryGetProperty("schema", out var schemaJson))
                    {
                        requestBodyType = GenerateDtoFromSchema(schemaJson, $"{actionName}Request");
                        parameters.Add($"[FromBody] {requestBodyType} body");
                    }
                }
            }

            // レスポンス型解析
            string returnType = "IActionResult";
            if (method.Value.TryGetProperty("responses", out var responses))
            {
                if (responses.TryGetProperty("200", out var resp200) &&
                    resp200.TryGetProperty("content", out var respContent) &&
                    respContent.TryGetProperty("application/json", out var respAppJson) &&
                    respAppJson.TryGetProperty("schema", out var respSchema))
                {
                    string dtoType = GenerateDtoFromSchema(respSchema, $"{actionName}Response");
                    returnType = $"ActionResult<{dtoType}>";
                }
            }

            sb.AppendLine($"    public {returnType} {actionName}({string.Join(", ", parameters)})");
            sb.AppendLine("    {");
            sb.AppendLine("        // TODO: 実装を追加");
            if (returnType.StartsWith("ActionResult<"))
                sb.AppendLine($"        return Ok(new {returnType[12..^1]}());");
            else
                sb.AppendLine("        return Ok();");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(outputDir, $"{controllerName}.cs"), sb.ToString());
    }
}

// 型マッピング
string MapOpenApiType(string type) => type switch
{
    "integer" => "int",
    "number" => "double",
    "boolean" => "bool",
    "string" => "string",
    _ => "object"
};

// スキーマからDTOクラス生成
string GenerateDtoFromSchema(JsonElement schema, string dtoName)
{
    if (generatedDtos.ContainsKey(dtoName))
        return dtoName;

    var sb = new StringBuilder();
    sb.AppendLine("using System;");
    sb.AppendLine("using System.ComponentModel.DataAnnotations;");
    sb.AppendLine();
    sb.AppendLine($"public class {dtoName}");
    sb.AppendLine("{");

    // requiredプロパティ取得
    HashSet<string> required = new();
    if (schema.TryGetProperty("required", out var requiredArray))
    {
        foreach (var req in requiredArray.EnumerateArray())
            required.Add(req.GetString()!);
    }

    if (schema.TryGetProperty("properties", out var props))
    {
        foreach (var prop in props.EnumerateObject())
        {
            string pname = Capitalize(prop.Name);
            string ptype = "object";
            var propSchema = prop.Value;
            var annotations = new List<string>();

            // [Required]
            if (required.Contains(prop.Name))
                annotations.Add("[Required]");

            // [StringLength]
            if (propSchema.TryGetProperty("maxLength", out var maxLen))
                annotations.Add($"[StringLength({maxLen.GetInt32()})]");

            // [EmailAddress] などformat対応
            if (propSchema.TryGetProperty("format", out var format))
            {
                if (format.GetString() == "email")
                    annotations.Add("[EmailAddress]");
            }

            if (propSchema.TryGetProperty("type", out var t))
                ptype = MapOpenApiType(t.GetString()!);

            foreach (var ann in annotations)
                sb.AppendLine($"    {ann}");
            sb.AppendLine($"    public {ptype} {pname} {{ get; set; }}");
        }
    }
    sb.AppendLine("}");
    File.WriteAllText(Path.Combine(dtoDir, $"{dtoName}.cs"), sb.ToString());
    generatedDtos[dtoName] = dtoName;
    return dtoName;
}

string GetControllerName(string path)
{
    var match = Regex.Match(path, @"^/?(\w+)");
    return match.Success ? $"{Capitalize(match.Groups[1].Value)}Controller" : "DefaultController";
}

string GetActionName(string method, string path)
{
    var name = path.Trim('/').Replace("/", "_");
    return $"{Capitalize(method)}{Capitalize(name)}";
}

string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
