namespace LinqToDB.SqlServer.Streaming
{
    public static class StreamingSettings
    {
		/// <summary>
		/// Buffer (chunk) size for uploads in bytes.
		/// Data will be read from stream into this buffer and separate write command will be sent to database for each chunk
		/// (10 MB by default). The less buffer size the more command will be sent to database. So this buffer shouldn't be small.
		/// </summary>
		public static int UploadBufferSize { get; set; }  = 10_000_000; // 10 MB

		/// <summary>
		/// Buffer size for streaming downloads.
		/// Default is 81920, somewhere below LOH objects size lower margin
		/// </summary>
		public static int DownloadBufferSize { get; set; }  = 81920;

		/// <summary>
		/// When downloading file and Task is cancelled, then OperationCancelledException is throwed by sql client.
		/// Set this to true if you want these exceptions to be thrown.
		/// </summary>
		public static bool PassThroughOperationCancelledExceptions { get; set; } = false;

		/// <summary>
		/// When not using array pool, then for each upload it will allocate new byte array with size of <see cref="StreamingSettings.UploadBufferSize"/>
		/// </summary>
		public static bool UseArrayPool { get; set; } = true;
	}
}
