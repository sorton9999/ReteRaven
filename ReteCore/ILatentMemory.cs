using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ReteCore
{
    public interface ILatentMemory
    {
        public IEnumerable<Token> Tokens { get; }
    }
}
