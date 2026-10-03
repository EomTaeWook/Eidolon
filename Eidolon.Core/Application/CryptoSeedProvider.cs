using System.Security.Cryptography;
using Eidolon.Core.Domain;
using Eidolon.Core.Infrastructure;

namespace Eidolon.Core.Application
{
    public class CryptoSeedProvider : ISeedProvider
    {
        public CryptoSeedProvider()
        {
        }

        public long Next()
        {
            return RandomNumberGenerator.GetInt32(int.MaxValue);
        }
    }
}
