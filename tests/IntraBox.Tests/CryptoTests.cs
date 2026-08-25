using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using IntraBox.Core;

namespace IntraBox.Tests
{
    /// <summary>加解密纯逻辑测试（CryptoHelper）。</summary>
    [TestClass]
    public class CryptoTests
    {
        [TestMethod]
        public void Symmetric_AES往返()
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)i;
            var plain = Encoding.UTF8.GetBytes("hello AES 你好");
            var cipher = CryptoHelper.SymmetricEncrypt(false, key, plain);
            CollectionAssert.AreEqual(plain, CryptoHelper.SymmetricDecrypt(false, key, cipher));
        }

        [TestMethod]
        public void Symmetric_DES往返()
        {
            var key = new byte[8];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
            var plain = Encoding.UTF8.GetBytes("hello DES");
            var cipher = CryptoHelper.SymmetricEncrypt(true, key, plain);
            CollectionAssert.AreEqual(plain, CryptoHelper.SymmetricDecrypt(true, key, cipher));
        }

        [TestMethod]
        public void Symmetric_两次加密_密文不同()
        {
            var key = new byte[32];
            var plain = Encoding.UTF8.GetBytes("same plain");
            var c1 = CryptoHelper.SymmetricEncrypt(false, key, plain);
            var c2 = CryptoHelper.SymmetricEncrypt(false, key, plain);
            CollectionAssert.AreNotEqual(c1, c2);
        }

        [TestMethod]
        public void DeriveKey_标准Base64长度_原样返回()
        {
            var key = new byte[32];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)i;
            var text = Convert.ToBase64String(key);
            CollectionAssert.AreEqual(key, CryptoHelper.DeriveKey(text, false));
        }

        [TestMethod]
        public void DeriveKey_非标准长度_SHA256派生到32字节()
        {
            var key = CryptoHelper.DeriveKey("my-secret-key", false);
            Assert.AreEqual(32, key.Length);
        }

        [TestMethod]
        public void FromBase64_合法与非法()
        {
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("hello"), CryptoHelper.FromBase64("aGVsbG8="));
            try
            {
                CryptoHelper.FromBase64("!!!");
                Assert.Fail("应抛出异常");
            }
            catch (InvalidOperationException) { }
        }

        [TestMethod]
        public void Rsa_往返()
        {
            using (var rsa = new RSACryptoServiceProvider(2048))
            {
                rsa.PersistKeyInCsp = false;
                var pub = rsa.ToXmlString(false);
                var priv = rsa.ToXmlString(true);
                var plain = Encoding.UTF8.GetBytes("hi");
                var cipher = CryptoHelper.RsaEncrypt(CryptoHelper.LoadRsa(pub, false), plain);
                var dec = CryptoHelper.RsaDecrypt(CryptoHelper.LoadRsa(priv, true), cipher);
                CollectionAssert.AreEqual(plain, dec);
            }
        }
    }
}
