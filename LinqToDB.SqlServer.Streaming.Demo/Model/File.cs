using LinqToDB.Mapping;

namespace LinqToDB.SqlServer.Streaming.Demo.Model
{
    [Table(Name = "Files")]
    public class File
    {
        [PrimaryKey]
        [Identity]
        public int Id { get; set; }

        [Column]
        public byte[] FileData { get; set; }

        [Column]
        public string FileName { get; set; }
    }
}
