using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PCL2Swap.Api;
using PclDlSwap.Payload.Host;

namespace PclDlSwap.Payload.Swap
{
    /// <summary>Process-local publish/subscribe bus shared by third-party modules.</summary>
    internal static class AddonMessageBus
    {
        private sealed class Subscription
        {
            public long Id;
            public string Source;
            public string Topic;
            public Action<SwapMessage> Handler;
            public volatile bool Active = true;
        }

        private static readonly object Gate = new object();
        private static readonly List<Subscription> Subscriptions = new List<Subscription>();
        private static long _nextId;

        public static IReadOnlyList<string> Topics
        {
            get
            {
                lock (Gate)
                    return Subscriptions.Where(item => item.Active && item.Topic != "*")
                        .Select(item => item.Topic)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                        .ToList();
            }
        }

        public static IDisposable Subscribe(string source, string topic,
            Action<SwapMessage> handler)
        {
            topic = NormalizeTopic(topic, allowWildcard: true);
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var subscription = new Subscription
            {
                Id = System.Threading.Interlocked.Increment(ref _nextId),
                Source = string.IsNullOrWhiteSpace(source) ? "host" : source,
                Topic = topic,
                Handler = handler
            };
            lock (Gate) Subscriptions.Add(subscription);
            return new SubscriptionToken(subscription);
        }

        public static int Publish(string source, string topic, object payload)
        {
            topic = NormalizeTopic(topic, allowWildcard: false);
            Subscription[] snapshot;
            lock (Gate) snapshot = Subscriptions.ToArray();

            var message = new SwapMessage
            {
                Topic = topic,
                Source = string.IsNullOrWhiteSpace(source) ? "host" : source,
                Payload = payload,
                TimestampUtc = DateTime.UtcNow
            };

            var delivered = 0;
            foreach (var subscription in snapshot)
            {
                if (!subscription.Active) continue;
                if (subscription.Topic != "*" &&
                    !string.Equals(subscription.Topic, topic, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    subscription.Handler(message);
                    delivered++;
                }
                catch (Exception ex)
                {
                    SwapLog.Warn($"[messaging] 订阅者抛错（{subscription.Source} ← " +
                                 $"{message.Source}:{topic}，已隔离）：{ex.Message}");
                    ModuleLog.Write(subscription.Source, "warn",
                        $"消息订阅回调抛错（topic={topic}, from={message.Source}）：{ex.Message}", ex);
                }
            }

            SwapLog.Info($"[messaging] {message.Source} → {topic}，送达 {delivered} 个订阅者。");
            return delivered;
        }

        public static int RemoveAll(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return 0;
            var removed = 0;
            lock (Gate)
            {
                foreach (var subscription in Subscriptions)
                {
                    if (!subscription.Active ||
                        !string.Equals(subscription.Source, source, StringComparison.OrdinalIgnoreCase))
                        continue;
                    subscription.Active = false;
                    removed++;
                }
                Subscriptions.RemoveAll(item => !item.Active);
            }
            return removed;
        }

        private static string NormalizeTopic(string topic, bool allowWildcard)
        {
            var value = (topic ?? string.Empty).Trim();
            if (value.Length == 0 || value.Length > 128)
                throw new ArgumentException("消息 topic 长度必须在 1..128 之间。", nameof(topic));
            if (allowWildcard && value == "*") return value;
            if (value == "*")
                throw new ArgumentException("Publish 不能使用通配 topic \"*\"。", nameof(topic));
            if (value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '.' || ch == ':' ||
                                  ch == '-' || ch == '_' || ch == '/')))
                throw new ArgumentException("消息 topic 只能包含字母、数字与 . : - _ /。", nameof(topic));
            return value;
        }

        private sealed class SubscriptionToken : IDisposable
        {
            private Subscription _subscription;

            public SubscriptionToken(Subscription subscription) => _subscription = subscription;

            public void Dispose()
            {
                var subscription = _subscription;
                if (subscription == null) return;
                _subscription = null;
                subscription.Active = false;
                lock (Gate) Subscriptions.Remove(subscription);
            }
        }
    }

    internal sealed class MessagingAccess : IAddonMessaging
    {
        private readonly string _source;
        private readonly string _permissionOwner;

        public MessagingAccess(string source, string permissionOwner = null)
        {
            _source = string.IsNullOrWhiteSpace(source) ? "host" : source;
            _permissionOwner = permissionOwner;
        }

        public IReadOnlyList<string> Topics
        {
            get
            {
                Require();
                return AddonMessageBus.Topics;
            }
        }

        public IDisposable Subscribe(string topic, Action<SwapMessage> handler)
        {
            Require();
            return AddonMessageBus.Subscribe(_source, topic, handler);
        }

        public int Publish(string topic, object payload = null)
        {
            Require();
            return AddonMessageBus.Publish(_source, topic, payload);
        }

        private void Require()
        {
            if (!string.IsNullOrWhiteSpace(_permissionOwner))
                SwapHost.Addons?.RequirePermission(_permissionOwner, "messaging");
        }
    }

    /// <summary>Path-confined storage rooted below one module directory.</summary>
    internal sealed class AddonFileStore : IAddonFiles
    {
        private readonly string _root;
        private readonly string _permissionOwner;

        public AddonFileStore(string root, string permissionOwner = null)
        {
            _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));
            _permissionOwner = permissionOwner;
        }

        public string Root
        {
            get
            {
                Require();
                return _root;
            }
        }

        public string GetPath(string relativePath)
        {
            Require();
            return Resolve(relativePath);
        }

        public bool Exists(string relativePath)
        {
            Require();
            var path = Resolve(relativePath);
            return File.Exists(path) || Directory.Exists(path);
        }

        public string ReadText(string relativePath)
        {
            Require();
            return File.ReadAllText(Resolve(relativePath, requireFile: true), new UTF8Encoding(false));
        }

        public void WriteText(string relativePath, string contents, bool append = false)
        {
            Require();
            var path = Resolve(relativePath);
            EnsureParent(path);
            if (append)
                File.AppendAllText(path, contents ?? string.Empty, new UTF8Encoding(false));
            else
                File.WriteAllText(path, contents ?? string.Empty, new UTF8Encoding(false));
        }

        public byte[] ReadBytes(string relativePath)
        {
            Require();
            return File.ReadAllBytes(Resolve(relativePath, requireFile: true));
        }

        public void WriteBytes(string relativePath, byte[] contents)
        {
            Require();
            if (contents == null) throw new ArgumentNullException(nameof(contents));
            var path = Resolve(relativePath);
            EnsureParent(path);
            File.WriteAllBytes(path, contents);
        }

        public bool Delete(string relativePath)
        {
            Require();
            var path = Resolve(relativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            if (!Directory.Exists(path)) return false;
            if (Directory.GetFileSystemEntries(path).Length != 0)
                throw new IOException("只能删除空目录，或先递归删除其中的文件。");
            Directory.Delete(path);
            return true;
        }

        public IReadOnlyList<string> List(string relativeDirectory = null, string searchPattern = "*",
            bool recursive = false)
        {
            Require();
            var directory = Resolve(relativeDirectory);
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            return Directory.GetFiles(directory, string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(path => Relative(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void Require()
        {
            if (!string.IsNullOrWhiteSpace(_permissionOwner))
                SwapHost.Addons?.RequirePermission(_permissionOwner, "files");
        }

        private string Resolve(string relativePath, bool requireFile = false)
        {
            var relative = (relativePath ?? string.Empty).Trim()
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relative))
                throw new ArgumentException("文件路径必须相对于模块存储根目录。", nameof(relativePath));

            var full = Path.GetFullPath(Path.Combine(_root, relative));
            var prefix = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.Equals(full, _root.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("文件路径越出了模块存储根目录。");
            if (requireFile && !File.Exists(full))
                throw new FileNotFoundException("模块文件不存在。", relativePath);
            return full;
        }

        private string Relative(string path)
        {
            var relative = path.Substring(_root.Length).TrimStart(Path.DirectorySeparatorChar);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        private static void EnsureParent(string path)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        }
    }
}

// GitHub@BlockConnectExp | BlockConnectLab@StarsailsClover
