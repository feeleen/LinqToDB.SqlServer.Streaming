# Streaming Extensions for LINQ to DB with SQL Server

The goal is to **upload and download large files** with **minimal memory allocation**.

By default, **SQL Server does not support true streaming for uploads**—unless you're on Windows **and** have the **FILESTREAM** feature enabled in your database.  
Additionally, **LINQ to DB does not natively support streaming or range-based downloads** from SQL Server.

## 1. Upload

When uploading a large file to the database, the entire file is typically loaded into memory before being sent to SQL Server.

The only way to avoid this is to **write the data in chunks** using SQL Server’s `.WRITE()` method (see [SQL Server documentation](https://learn.microsoft.com/en-us/sql/t-sql/queries/update-transact-sql?view=sql-server-ver16#using-the-write-clause)).

However, this approach requires **multiple separate SQL commands**—the number depending on the file size and chunk size.  
Therefore, it’s **only worthwhile for files larger than 20–50 MB**, where memory usage would otherwise become a concern.

## 2. Download

When downloading binary data via LINQ to DB, there is **no built-in way to stream directly from SQL Server** (e.g., to `HttpResponse.Body` without buffering the entire file in memory).

This extension **adds true streaming support for downloads**, enabling efficient, low-memory file delivery.

# Examples

Sample table:

```sql
CREATE TABLE [dbo].[Files](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[FileData] [varbinary](max) NULL,
)
```

DbContext:

```csharp
public class DbContext : DataContext
{
	public DbContext(string connectionString) : base(ProviderName.SqlServer, connectionString)
	{
		CommandTimeout = 600;
	}
}
```


FileService:

```csharp
private DbContext db;

public async Task<int> Upload(Stream? fileStream, string fileName, CancellationToken cancellationToken)
{
	using (var t = await db.BeginTransactionAsync()) // we need a transaction here to avoid partially loaded data in case of errors
	{
		var file = new Model.File { FileData = new byte[0] };
		var id = db.InsertWithInt32Identity(file);

		var chunksCount = await db.StreamUpload<Model.File>(id, x => x.FileData, fileStream, cancellationToken: cancellationToken);
		await t.CommitTransactionAsync(cancellationToken);

		return id;
	}
}

public async Task CopyToStream(long id, Stream destStream, CancellationToken cancellationToken)
{
	await db.CopyToStream<Model.File>(id, x => x.FileData, destStream, cancellationToken: cancellationToken);
}
```

Additional settings available:

```csharp
StreamingSettings.UploadBufferSize = 11_000_000; // in bytes
StreamingSettings.DownloadBufferSize = 81920; // in bytes
StreamingSettings.PassThroughOperationCancelledExceptions = false;
StreamingSettings.UseArrayPool = true;
```

Example of file upload (~ 890 MB) with buffer = 11 MB

```text
File length: 890536451 bytes
Content-Type: application/zip
1: Bytes read: 11000000,
2: Bytes read: 11000000,
3: Bytes read: 11000000,
...
81: Bytes read: 10536451,
Total: 890536451
File successfully uploaded for: 00:00:14.7621856
```


