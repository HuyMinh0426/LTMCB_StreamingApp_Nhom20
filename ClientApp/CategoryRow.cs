using System.Collections.Generic;

namespace ClientApp
{
    public class CategoryRow
    {
        public string CategoryName { get; set; }
        public List<MovieInfo> Movies { get; set; }
    }
}