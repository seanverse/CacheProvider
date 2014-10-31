﻿using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using CacheProvider.Model;

namespace CacheProvider.Memory
{
    public class MemoryCacheProvider : CacheProvider
    {
        private static MemoryCache _cache;

        private int _cacheExpirationTime;
        private bool _isEnabled;
        private static readonly object Sync = new object();

        public MemoryCacheProvider()
            : this(MemoryCache.Default)
        {
        }

        private MemoryCacheProvider(MemoryCache cache)
        {
            _cache = cache;
        }

        /// <summary>
        ///     Initialize from config
        /// </summary>
        /// <param name="name"></param>
        /// <param name="config">Config properties</param>
        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            var timeout = config["timeout"];
            if (string.IsNullOrEmpty(timeout))
            {
                timeout = "10";
            }

            _isEnabled = true;
            var enabled = config["enable"];
            if (enabled == null)
            {
                _isEnabled = true;
            }
            else
            {
                bool.TryParse(config["enable"], out _isEnabled);  
            }

            _cacheExpirationTime = 60;

            if (!int.TryParse(timeout, out _cacheExpirationTime))
            {
                throw new ConfigurationErrorsException("invalid timeout value");
            }
        }

        /// <summary>
        ///     Get from cache.
        /// </summary>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="region"></param>
        /// <returns>
        ///     An object instance with the Cache Value corresponding to the entry if found, else null
        /// </returns>
        public override async Task<object> Get(object cacheKey, string region)
        {
            if (!_isEnabled)
            {
                return null;
            }

            var item = (BaseModel)_cache.Get(MemoryUtilities.CombinedKey(cacheKey, region));

            return await MemoryStreamHelper.DeserializeObject(item.CacheObject);
        }

        /// <summary>
        ///     Gets the specified cache key.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="region"></param>
        /// <returns>An Instance of T if the entry is found, else null.</returns>
        public override async Task<T> Get<T>(object cacheKey, string region)
        {
            return (T) await Get(cacheKey, region);
        }

        /// <summary>
        /// Check if the item exist
        /// </summary>
        /// <param name="cacheKey"></param>
        /// <param name="region"></param>
        /// <returns>true false</returns>
        public override Task<bool> Exist(object cacheKey, string region)
        {
            return !_isEnabled ? 
                Task.FromResult(false) : 
                Task.FromResult(_cache[MemoryUtilities.CombinedKey(cacheKey, region)] != null);
        }

        /// <summary>
        ///     Add to cache.
        /// </summary>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="cacheObject">The cache object.</param>
        /// <param name="region"></param>
        /// <param name="expirationInMinutes"></param>
        /// <returns>True if successful else false.</returns>
        public override async Task<bool> Add(object cacheKey, object cacheObject, string region, int expirationInMinutes = 15)
        {
            if (!_isEnabled)
            {
                return true;
            }

            var expireCacheTime = expirationInMinutes == 15 ? _cacheExpirationTime : expirationInMinutes;

            var cacheItemPolicy = new CacheItemPolicy
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(expireCacheTime)
            };

            return await CreateUpdateItem(cacheKey, cacheObject, region, expireCacheTime, cacheItemPolicy);
        }

        /// <summary>
        ///     Add to cache.
        /// </summary>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="cacheObject">The cache object.</param>
        /// <param name="region"></param>
        /// <param name="allowSliddingTime">Updates the expiration x minutes from last write or reed</param>
        /// <param name="expirationInMinutes"></param>
        /// <returns>True if successful else false.</returns>
        public override async Task<bool> Add(object cacheKey, object cacheObject, string region, bool allowSliddingTime, int expirationInMinutes = 15)
        {
            if (!_isEnabled)
            {
                return true;
            }

            var expireCacheTime = expirationInMinutes == 15 ? _cacheExpirationTime : expirationInMinutes;
            var cacheItemPolicy = new CacheItemPolicy
            {
                SlidingExpiration = TimeSpan.FromMinutes(expireCacheTime)
            };

            return await CreateUpdateItem(cacheKey, cacheObject, region, expireCacheTime, cacheItemPolicy, allowSliddingTime);
        }

        /// <summary>
        ///     Add an item to the cache and will need to be removed manually
        /// </summary>
        /// <param name="cacheKey"></param>
        /// <param name="cacheObject"></param>
        /// <param name="region"></param>
        /// <returns>true or false</returns>
        public override async Task<bool> AddPermanent(object cacheKey, object cacheObject, string region)
        {
            if (!_isEnabled)
            {
                return true;
            }

            var cacheItemPolicy = new CacheItemPolicy();

            return await CreateUpdateItem(cacheKey, cacheObject, region, 10000, cacheItemPolicy);
        }

        /// <summary>
        ///     Remove from cache.(region not supported in memorycache)
        /// </summary>
        /// <param name="cacheKey">The cache key.</param>
        /// <param name="region"></param>
        /// <returns>True if successful else false.</returns>
        public override Task<bool> Remove(object cacheKey, string region)
        {
            if (!_isEnabled)
            {
                Task.FromResult(true);
            }

            _cache.Remove(MemoryUtilities.CombinedKey(cacheKey, region));
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Remove from cache.(region not supported in memorycache)
        /// </summary>
        /// <returns>True if successful else false.</returns>
        public override Task<bool> RemoveAll()
        {
            if (!_isEnabled)
            {
                Task.FromResult(true);
            }

            _cache.Dispose();
            _cache = MemoryCache.Default;
            return Task.FromResult(true);
        }

        /// <summary>
        ///     Remove from cache.(region not supported in memorycache)
        /// </summary>
        /// <param name="region"></param>
        /// <returns>True if successful else false.</returns>
        public override Task<bool> RemoveAll(string region)
        {
            return RemoveAll();
        }

        /// <summary>
        ///     Remove from cache.(region not supported in memorycache)
        /// </summary>
        /// <param name="region"></param>
        /// <returns>True if successful else false.</returns>
        public override Task<bool> RemoveExpired(string region)
        {
            return RemoveAll();
        }

        /// <summary>
        ///     Gets the cache count by region (region not supported in memorycache)
        /// </summary>
        public override async Task<long> Count(string region)
        {
            if (!_isEnabled)
            {
                return 0;
            }

           return await Task.Factory.StartNew(() => _cache.Count());
        }

        #region Helpers
        private static async Task<bool> CreateUpdateItem(object cacheKey, object cacheObject, string region, int expireCacheTime, CacheItemPolicy cacheItemPolicy, bool allowSliddingTime = false)
        {
            var cacheData = await MemoryStreamHelper.SerializeObject(cacheObject);
            var key = MemoryUtilities.CombinedKey(cacheKey, region);
            var expireTime = DateTime.UtcNow.AddMinutes(expireCacheTime);
            var item = new BaseModel
            {
                CacheKey = key,
                Expires = expireTime,
                CacheObject = cacheData,
                AllowSliddingTime = allowSliddingTime
            };

            bool results;
            lock (Sync)
            {
                results = _cache.Add(key, item, cacheItemPolicy);
            }

            return results;
        }


        #endregion
    }
}