# OpenApiTools

## 概要
OpenApiToolsは、OpenAPI（Swagger）JSON定義からASP.NET Core WebAPI用のコントローラーおよびDTOクラスを自動生成するツールです。パラメータやリクエスト/レスポンス型、データ注釈（Data Annotations）にも対応しています。

## 前提条件
- .NET 8 SDK
- OpenAPI 3.0/3.1 形式のJSONファイル

## 使い方
1. `openapi.json`（OpenAPI定義ファイル）をプロジェクトルートに配置します。
2. コマンドラインで以下を実行します：
	dotnet run --project OpenApiTools
3. `GeneratedControllers` ディレクトリにコントローラー、`GeneratedDtos` ディレクトリにDTOクラスが出力されます。

## コマンドライン引数の使用
OpenApiToolsは、コマンドライン引数を使用して、OpenAPI定義ファイル、コントローラー出力先、DTO出力先を指定できます。

### 使用例
    OpenApiTools openapi.json MyControllers MyDtos

- 引数を省略した場合は、デフォルトの `openapi.json`、`GeneratedControllers`、`GeneratedDtos` が使用されます。

## 機能
- OpenAPIのpathsからエンドポイントごとにコントローラーを生成
- パス/クエリ/ヘッダーパラメータをメソッド引数として自動生成
- リクエストボディ・レスポンス型のDTOクラスを自動生成
- 必要なデータ注釈（[Required]、[StringLength]、[Range]など）を自動付与
- OpenAPI拡張（x-）やカスタムデータ注釈にも対応可能

## カスタムデータ注釈の追加方法
1. `System.ComponentModel.DataAnnotations.ValidationAttribute` を継承した独自の属性クラスを作成します。
2. `Program.cs`のDTO生成ロジックで、OpenAPI拡張やプロパティ名などの条件に応じてカスタム属性を付与します。
3. 例：
	if (propSchema.TryGetProperty("x-my-custom", out var custom) && custom.GetBoolean()) annotations.Add("[MyCustom]");

## 注意事項
- OpenAPIスキーマの複雑な型（ネスト、oneOf/allOf等）には追加実装が必要な場合があります。
- 生成されたコードはプロジェクトのコーディング規約に合わせて調整してください。

## multipart/form-data 対応

OpenApiToolsは、OpenAPI定義で `multipart/form-data` が指定されたリクエストボディにも対応しています。

- `type: string`, `format: binary` の場合は `IFormFile` 型の引数が自動生成されます。
- 配列（`type: array` かつ `items.type: string`, `items.format: binary`）の場合は `List<IFormFile>` 型の引数が生成されます。
- その他のフォームデータも `[FromForm]` 属性付きで自動的に引数化されます。

### OpenAPIスキーマ例

```yaml
RequestBody:
  content:
    multipart/form-data:
      schema:
        type: object
        properties:
          file:
            type: string
            format: binary
          id:
            type: integer
            format: int32
          tags:
            type: array
            items:
              type: string
```

```csharp
public class SampleRequest
{
    public IFormFile File { get; set; }
    public int Id { get; set; }
    public List<string> Tags { get; set; }
}
```

### 生成されるコントローラーメソッド例
```csharp
[HttpPost("upload")]
public async Task<IActionResult> UploadFile(
    [FromForm] SampleRequest request)
{
    // アップロード処理
}
```

- 必要に応じて `using Microsoft.AspNetCore.Http;` を追加してください。
- 複数ファイルの場合は `List<IFormFile>` で受け取れます。

### バリデーション例
```csharp
[HttpPost]
[Route("upload")]
public IActionResult Upload([FromForm] IFormFile file)
{
    if (file == null || file.Length == 0)
        return BadRequest("ファイルが選択されていません。");

    // 画像拡張子・MIMEタイプの許可リスト
    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif" };
    var allowedContentTypes = new[] { "image/jpeg", "image/png", "image/gif" };

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExtensions.Contains(ext))
        return BadRequest("許可されていない画像ファイル形式です。");

    if (!allowedContentTypes.Contains(file.ContentType))
        return BadRequest("許可されていない画像MIMEタイプです。");

    // ファイルサイズ制限（例: 5MB）
    if (file.Length > 5 * 1024 * 1024)
        return BadRequest("ファイルサイズが大きすぎます。");

    // 画像ファイルの保存や処理
    return Ok("画像アップロード成功");
}
```

## ライセンス
このツールはMITライセンスで提供されます。

### 開発者
- 名前: Copilot(99%)

