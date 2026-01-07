using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using System.Buffers;
using System.Data;
using System.Linq.Expressions;

namespace LinqToDB.SqlServer.Streaming
{
	public static class StreamingExtentions
	{
		/// <summary>
		/// Writes binary file to database with imitation of stream upload by writing data with several separated requests
		/// while reading from input stream by chunks with specified buffer size (<see cref="StreamingSettings.UploadBufferSize">)
		/// You should open transaction before calling this method to prevent partial data writes in case of errors.
		/// </summary>
		/// <typeparam name="TEntity"></typeparam>
		/// <param name="db"></param>
		/// <param name="recordId">Id of updated record (you must insert it before calling stream upload method)</param>
		/// <param name="propertyExpression"></param>
		/// <param name="fileStream">Input stream</param>
		/// <param name="cancellationToken"></param>
		/// <returns>Returns written chunks count</returns>
		/// <exception cref="ArgumentNullException"></exception>
		public static async Task<int> StreamUpload<TEntity>(
			this DataContext db,
			long recordId,
			Expression<Func<TEntity, byte[]>> propertyExpression,
			Stream? fileStream,
			CancellationToken cancellationToken = default) where TEntity : class
		{
			if (fileStream == null)
				throw new ArgumentNullException(nameof(fileStream));

			var metadata = GetMetadata(db, propertyExpression);

			int chunkSize = StreamingSettings.UploadBufferSize;
			bool useArrayPool = StreamingSettings.UseArrayPool;

			byte[] buffer = useArrayPool ? ArrayPool<byte>.Shared.Rent(chunkSize) : new byte[chunkSize];
			
			try
			{
				long offset = 0;
				int bytesRead = 0;
				int chunksCount = 0;

				while ((bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize /*buffer.Length - if we want use the full size of buffer from arrayPool? not shure */, cancellationToken)) > 0)
				{
					var paramArray = new DataParameter[] {
						new DataParameter("@chunk", buffer, DataType.Image, bytesRead),
						new DataParameter("@offset",offset , DataType.Int64),
						new DataParameter("@length", bytesRead, DataType.Int32),
						new DataParameter("@id", recordId, DataType.Int64),
					};

					var cmd = await db.ExecuteAsync(
						$@"UPDATE {(string.IsNullOrEmpty(metadata.SchemaName) ? $"[{metadata.TableName}]" : $"[{metadata.SchemaName}].[{metadata.TableName}]")}
							SET [{metadata.DataColumnName}] .WRITE(@chunk, @offset, @length) WHERE [{metadata.KeyColumnName}] = @id",
						cancellationToken, paramArray);

					offset += bytesRead;
					chunksCount++;
					Console.WriteLine($"{chunksCount}: Bytes read: {bytesRead},");
				}
				Console.WriteLine($"Total: {offset}");
				
				return chunksCount;
			}
			finally
			{
				if (useArrayPool)
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}
			}
		}

		/// <summary>
		/// Streaming read from database
		/// </summary>
		/// <typeparam name="TEntity"></typeparam>
		/// <param name="db"></param>
		/// <param name="recordId">Id of record to read data from</param>
		/// <param name="propertyExpression">Name of the data property</param>
		/// <param name="destStream">Stream to write data to</param>
		/// <param name="startByte"></param>
		/// <param name="endByte"></param>
		/// <param name="cancellationToken"></param>
		/// <returns></returns>
		/// <exception cref="ArgumentNullException"></exception>
		/// <exception cref="FileNotFoundException"></exception>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		public static async Task CopyToStream<TEntity>(
			this DataContext db,
			long recordId,
			Expression<Func<TEntity, byte[]>> propertyExpression,
			Stream destStream,
			long? startByte = null, long? endByte = null, CancellationToken cancellationToken = default)
		{
			if (destStream == null)
				throw new ArgumentNullException(nameof(destStream));

			var metadata = GetMetadata(db, propertyExpression);

			var sql = @$"SELECT FileData
				from {(string.IsNullOrEmpty(metadata.SchemaName) ? $"[{metadata.TableName}]" : $"[{metadata.SchemaName}].[{metadata.TableName}]")}
				WHERE [{metadata.KeyColumnName}] = @Id";

			int bufferSize = StreamingSettings.DownloadBufferSize;
			bool useArrayPool = StreamingSettings.UseArrayPool;

			using var reader = await db.ExecuteReaderAsync(sql, CommandType.Text, CommandBehavior.SequentialAccess, cancellationToken, new DataParameter("Id", recordId));

			if (!await reader.Reader.ReadAsync())
				throw new FileNotFoundException($"Record with key {recordId} not found");

			try
			{
				{
					var contentOrdinal = reader.Reader.GetOrdinal(metadata.DataColumnName);
					
					byte[] buffer = useArrayPool ? ArrayPool<byte>.Shared.Rent(bufferSize) : new byte[bufferSize];
					try
					{
						long pos = startByte ?? 0;

						if (startByte.HasValue && endByte.HasValue)
						{
							if (startByte < 0 || endByte < startByte)
								throw new ArgumentOutOfRangeException($"Invalid range: {startByte} - {endByte}");

							long totalBytesToRead = endByte.Value - pos + 1;
							long totalBytesRead = 0;

							while (!cancellationToken.IsCancellationRequested && totalBytesRead < totalBytesToRead)
							{
								long bytesRemaining = totalBytesToRead - totalBytesRead;
								int bytesToRead = (int)Math.Min(bufferSize, bytesRemaining);

								long bytesRead = reader.Reader.GetBytes(contentOrdinal, pos, buffer, 0, bytesToRead);

								if (bytesRead == 0)
									break;

								await destStream.WriteAsync(buffer.AsMemory(0, (int)bytesRead), cancellationToken);

								pos += bytesRead;
								totalBytesRead += bytesRead;
							}
						}
						else
						{
							int bytesRead;

							while (!cancellationToken.IsCancellationRequested && (bytesRead = (int)reader.Reader.GetBytes(contentOrdinal, pos, buffer, 0, bufferSize)) > 0)
							{
								await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
								pos += bytesRead;
							}
						}
					}
					finally
					{
						if (useArrayPool)
						{
							ArrayPool<byte>.Shared.Return(buffer);
						}
					}
				}
			}
			catch (OperationCanceledException ex)
			{
				if (StreamingSettings.PassThroughOperationCancelledExceptions)
					throw;
			}
		}

		private static (string SchemaName, string TableName, string DataColumnName, string KeyColumnName) GetMetadata<TEntity>(this DataContext db, Expression<Func<TEntity, byte[]>> propertyExpression)
		{
			var mappingSchema = db.MappingSchema;
			var descriptor = mappingSchema.GetEntityDescriptor(typeof(TEntity));

			string colName = null;

			if (propertyExpression.Body is MemberExpression memberExpression)
			{
				colName = descriptor.Columns.Where(c => c.MemberName == memberExpression.Member.Name).Select(x => x.ColumnName).FirstOrDefault();

				if (colName == null)
				{
					throw new ArgumentException($"Property {memberExpression.Member.Name} not mapped to a column", memberExpression.Member.Name);
				}
			}
			else
			{
				throw new ArgumentException(
					"Expression must be a direct property access returning byte[].",
					nameof(propertyExpression));
			}

			// Gettting Key column
			var keyMembers = descriptor.Columns.Where(c => c.IsPrimaryKey).ToArray();
			if (keyMembers.Length == 0)
				throw new InvalidOperationException($"Type {typeof(TEntity)} has no primary key defined");
			if (keyMembers.Length > 1)
				throw new InvalidOperationException($"Type {typeof(TEntity)} has more than one primary keys defined, but only one supported");

			return (descriptor.SchemaName, descriptor.TableName, colName, keyMembers[0].ColumnName);
		}
	}
}
