using LinqToDB.SqlServer.Streaming.Demo.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Threading;

namespace LinqToDB.SqlServer.Streaming.Demo.Controllers
{
	[ApiController]
	[Route("api/files")]
	public class FilesController : ControllerBase
	{
		private readonly FileService FileService;
		public FilesController(FileService fileService)
		{
			FileService = fileService;
		}

		[HttpPost("upload")]
		[RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
		public async Task<IActionResult> UploadFile(IFormFile file, CancellationToken cancellationToken)
		{
			if (file == null || file.Length == 0)
			{
				return BadRequest("File is empty");
			}

			Console.WriteLine($"Name: {file.FileName}");
			Console.WriteLine($"File length: {file.Length} bytes"); 
			Console.WriteLine($"Content-Type: {file.ContentType}");

			var sw = new Stopwatch();
			sw.Start();
			try
			{
				var id = await FileService.Upload(file.OpenReadStream(), file.FileName, cancellationToken);

				return Ok(new { Message = "File successfully uploaded", FileName = file.FileName, Id = id });
			}
			catch (Exception ex)
			{
				return StatusCode(500, $"Error during upload: {ex.Message}");
			}
			finally
			{
				sw.Stop();
				Console.WriteLine($"File successfully uploaded for: {sw.Elapsed}");
			}
		}

		[HttpGet("download/{id:long}")]
		[RequestFormLimits(ValueLengthLimit = int.MaxValue, MultipartBodyLengthLimit = int.MaxValue)]
		public async Task DownloadFile(int id, CancellationToken cancellationToken)
		{
			try
			{
				await FileService.CopyToStream(id, Response.Body, cancellationToken);
			}
			catch (FileNotFoundException ex)
			{
				Response.StatusCode = StatusCodes.Status304NotModified;
				return;
			}
		}
	}
}
