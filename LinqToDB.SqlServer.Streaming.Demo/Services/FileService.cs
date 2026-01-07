using Azure;
using Microsoft.Data.SqlClient;
using System;
using System.Buffers;
using System.Data;
using System.Threading;
using System.Transactions;
using LinqToDB.SqlServer.Streaming;

namespace LinqToDB.SqlServer.Streaming.Demo.Services
{
	public class FileService
	{
		private readonly DbContext db;

		public FileService(IConfiguration configuration, DbContext db)
		{
			this.db = db;
		}

		public async Task<int> Upload(Stream? fileStream, string fileName, CancellationToken cancellationToken)
		{
			using (var t = await db.BeginTransactionAsync())
			{
				var file = new Model.File { FileName = fileName, FileData = new byte[0] };
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
	}
}
