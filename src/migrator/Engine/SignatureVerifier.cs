
//using System.Security.Cryptography.X509Certificates;
//using System.Text;
//using NSec.Cryptography;


//namespace migrator.Engine;






//public static class SignatureVerifier
//{
//    public static bool Verify(Migration m, byte[] publicKey)
//    {
//        if (string.IsNullOrWhiteSpace(m.Header?.Signature))
//            return false;

//        try
//        {
//            var payload = m.Header.BuildSigningPayload(m.Checksum);
//            var payloadBytes = Encoding.UTF8.GetBytes(payload);
//            var sigBytes = Convert.FromBase64String(m.Header.Signature);

//            var algorithm = SignatureAlgorithm.Ed25519;
//            var pub = PublicKey.Import(algorithm, publicKey, KeyBlobFormat.RawPublicKey);

//            return algorithm.Verify(pub, payloadBytes, sigBytes);
//        }
//        catch
//        {
//            return false;
//        }
//    }
//}

