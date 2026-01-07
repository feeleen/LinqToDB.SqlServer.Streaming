using LinqToDB.Data;

namespace LinqToDB.SqlServer.Streaming.Demo.Services
{
	public class DbContext : DataContext
	{
		public DbContext(string connectionString) : base(ProviderName.SqlServer, connectionString)
		{
			CommandTimeout = 600;
		}
	}
}
