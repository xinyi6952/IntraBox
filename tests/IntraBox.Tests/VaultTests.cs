using System.Collections.Generic;
using IntraBox.Core;
using IntraBox.Modules.Vault;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IntraBox.Tests
{
    [TestClass]
    public class VaultTests
    {
        [TestMethod]
        public void ParsePlatforms_斜杠顿号逗号_拆成去重列表()
        {
            var list = VaultText.ParsePlatforms(" GitLab / Jenkins、禅道, GitLab ");
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("GitLab", list[0]);
            Assert.AreEqual("Jenkins", list[1]);
            Assert.AreEqual("禅道", list[2]);
        }

        [TestMethod]
        public void ParsePlatforms_空_返回空列表()
        {
            Assert.AreEqual(0, VaultText.ParsePlatforms("").Count);
            Assert.AreEqual(0, VaultText.ParsePlatforms(null).Count);
            Assert.AreEqual(0, VaultText.ParsePlatforms("  /  、 ").Count);
        }

        [TestMethod]
        public void FormatPlatforms_用斜杠拼接()
        {
            Assert.AreEqual("GitLab / 禅道", VaultText.FormatPlatforms(new List<string> { "GitLab", "禅道" }));
        }

        [TestMethod]
        public void CopyRecord_无URL不输出该行()
        {
            string noUrl = VaultText.CopyRecord(new List<string> { "GitLab" }, "", "alice", "secret");
            Assert.AreEqual("平台: GitLab\r\n账号: alice\r\n密码: secret", noUrl);
            Assert.IsTrue(noUrl.IndexOf("URL:", System.StringComparison.Ordinal) < 0);
            string withUrl = VaultText.CopyRecord(new List<string> { "GitLab", "Jenkins" }, " https://ex ", "ops", "pw");
            Assert.AreEqual("平台: GitLab / Jenkins\r\nURL: https://ex\r\n账号: ops\r\n密码: pw", withUrl);
            Assert.AreEqual("平台: \r\n账号: alice\r\n密码: secret",
                VaultText.CopyAccountPassword("alice", "secret"));
        }

        [TestMethod]
        public void MatchesSearch_不匹配密码与密文账号()
        {
            var entries = new List<VaultEntry>
            {
                new VaultEntry
                {
                    Platforms = new List<string> { "GitLab" },
                    Account = "alice",
                    Password = "SuperSecret",
                    Masked = false
                },
                new VaultEntry
                {
                    Platforms = new List<string> { "Jenkins" },
                    Account = "hidden-user",
                    Password = "pw",
                    Masked = true
                }
            };
            Assert.IsTrue(VaultText.MatchesSearch("内网", "", entries, "GitLab"));
            Assert.IsTrue(VaultText.MatchesSearch("内网", "", entries, "alice"));
            Assert.IsFalse(VaultText.MatchesSearch("内网", "", entries, "SuperSecret"));
            Assert.IsFalse(VaultText.MatchesSearch("内网", "", entries, "hidden-user"));
            Assert.IsTrue(VaultText.MatchesSearch("内网 Git", "说明", entries, "内网"));
            entries[0].Url = "https://gitlab.example.com";
            Assert.IsTrue(VaultText.MatchesSearch("x", "", entries, "gitlab.example"));
        }

        [TestMethod]
        public void ValidateEntry_账号或密码缺一不可()
        {
            var e = new VaultEntry { Account = "a", Password = "" };
            Assert.IsNotNull(VaultText.ValidateEntry(e));
            e.Password = "p";
            Assert.IsNull(VaultText.ValidateEntry(e));
            Assert.IsNull(VaultText.ValidateEntry(new VaultEntry()));
        }

        [TestMethod]
        public void Crypto_口令往返与错误口令()
        {
            byte[] salt = VaultCrypto.NewSalt();
            byte[] key = VaultCrypto.DeriveKey("demo-pass", salt);
            string ver = VaultCrypto.MakeVerifier(key);
            Assert.IsTrue(VaultCrypto.CheckVerifier(key, ver));
            byte[] wrong = VaultCrypto.DeriveKey("other-pass", salt);
            Assert.IsFalse(VaultCrypto.CheckVerifier(wrong, ver));
            string cipher = VaultCrypto.EncryptString(key, "账号 alice");
            Assert.AreEqual("账号 alice", VaultCrypto.DecryptString(key, cipher));
        }

        [TestMethod]
        public void PackForDisk_密文行不含明文()
        {
            byte[] salt = VaultCrypto.NewSalt();
            byte[] key = VaultCrypto.DeriveKey("lock-pass", salt);
            var e = new VaultEntry
            {
                Masked = true,
                Account = "alice",
                Password = "pw1",
                Platforms = new List<string> { "GitLab" }
            };
            VaultText.PackForDisk(e, key);
            Assert.AreEqual("", e.Account);
            Assert.AreEqual("", e.Password);
            Assert.IsFalse(string.IsNullOrEmpty(e.AccountEnc));
            string err;
            Assert.IsTrue(VaultText.TryReveal(e, key, out err));
            Assert.AreEqual("alice", e.Account);
            Assert.AreEqual("pw1", e.Password);
        }

        [TestMethod]
        public void TryReveal_尚未打包_保留内存明文()
        {
            byte[] salt = VaultCrypto.NewSalt();
            byte[] key = VaultCrypto.DeriveKey("lock-pass", salt);
            var e = new VaultEntry
            {
                Masked = true,
                Account = "alice",
                Password = "pw1"
            };
            string err;
            Assert.IsTrue(VaultText.TryReveal(e, key, out err));
            Assert.AreEqual("alice", e.Account);
            Assert.AreEqual("pw1", e.Password);
        }

        [TestMethod]
        public void SealFields_保留明文且密文可解密()
        {
            byte[] salt = VaultCrypto.NewSalt();
            byte[] key = VaultCrypto.DeriveKey("lock-pass", salt);
            var e = new VaultEntry
            {
                Masked = true,
                Account = "alice",
                Password = "pw1"
            };
            VaultText.SealFields(e, key);
            Assert.AreEqual("alice", e.Account);
            Assert.AreEqual("pw1", e.Password);
            Assert.IsFalse(string.IsNullOrEmpty(e.AccountEnc));
            e.Account = "";
            e.Password = "";
            e.Masked = false;
            string err;
            Assert.IsTrue(VaultText.TryReveal(e, key, out err));
            Assert.AreEqual("alice", e.Account);
            Assert.AreEqual("pw1", e.Password);
        }

        [TestMethod]
        public void CopyAll_多条_空行分隔且可往返()
        {
            var entries = new List<VaultEntry>
            {
                new VaultEntry
                {
                    Platforms = new List<string> { "GitLab", "Jenkins" },
                    Url = "https://ex",
                    Account = "ops",
                    Password = "pw"
                },
                new VaultEntry { Account = "bob", Password = "b2", Platforms = new List<string> { "禅道" } }
            };
            string text = VaultText.CopyAll(entries);
            Assert.AreEqual(
                "平台: GitLab / Jenkins\r\nURL: https://ex\r\n账号: ops\r\n密码: pw\r\n\r\n平台: 禅道\r\n账号: bob\r\n密码: b2",
                text);
            var back = VaultText.ParseExport(text);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("ops", back[0].Account);
            Assert.AreEqual("https://ex", back[0].Url);
            Assert.AreEqual(2, back[0].Platforms.Count);
            Assert.AreEqual("bob", back[1].Account);
            Assert.AreEqual("b2", back[1].Password);
            Assert.IsFalse(back[0].Masked);
        }

        [TestMethod]
        public void ParseExport_全角冒号与不完整行()
        {
            string text = "平台：GitLab\n账号：alice\n密码：secret\n\n账号: onlyacc\n\n平台: x\n账号: a\n密码: p";
            var list = VaultText.ParseExport(text);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("alice", list[0].Account);
            Assert.AreEqual("a", list[1].Account);
            Assert.AreEqual(0, VaultText.ParseExport("").Count);
            Assert.AreEqual(0, VaultText.ParseExport("说明: hi").Count);
        }
    }
}
