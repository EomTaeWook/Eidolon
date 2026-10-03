using System.Security.Cryptography;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.Core.Application
{
    public interface ISeedProvider
    {
        long Next();
    }
}
