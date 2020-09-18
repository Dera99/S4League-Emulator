using System.Collections.Concurrent;
using System.Threading;
using Org.BouncyCastle.Security;
using ProudNetSrc;

namespace NeoNetsphere
{
  using System;
  using System.Collections.Generic;
  using System.Data;
  using System.Threading.Tasks;
  using Dapper.FastCrud;
  using Dapper.FastCrud.Configuration.StatementOptions.Builders;
  using Serilog;
  using Serilog.Core;

  class DbUtil
  {
    private static readonly ILogger Logger = Log.ForContext(Constants.SourceContextPropertyName, nameof(DbUtil));
    public static readonly MaxUseLock Sync = new MaxUseLock(100); // Allow max 100 db actions at a time

    public static IDbTransaction BeginTransaction(IDbConnection db)
    {
     // using (Sync.Lock())
      {
        try
        {
          return db.BeginTransaction();
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return null;
      }
    }

    public static TEntity Get<TEntity>(IDbConnection db, TEntity entityKeys,
        Action<ISelectSqlSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (Sync.Lock())
      {
        try
        {
          return db.Get(entityKeys, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return entityKeys;
      }
    }

    public static async Task<TEntity> GetAsync<TEntity>(IDbConnection db, TEntity entityKeys,
        Action<ISelectSqlSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (await Sync.LockAsync())
      {
        try
        {
          return await db.GetAsync(entityKeys, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return entityKeys;
      }
    }

    public static int BulkDelete<TEntity>(IDbConnection db,
        Action<IConditionalBulkSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (Sync.Lock())
      {
        try
        {
          return db.BulkDelete(statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return -1;
      }
    }

    public static async Task<int> BulkUpdateAsync<TEntity>(IDbConnection db, TEntity updateData,
        Action<IConditionalBulkSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
    //  using (await Sync.LockAsync())
      {
        try
        {
          return await db.BulkUpdateAsync(updateData, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return -1;
      }
    }

    public static int BulkUpdate<TEntity>(IDbConnection db, TEntity updateData,
        Action<IConditionalBulkSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    { 
     // using (Sync.Lock())
      {
        try
        {
          return db.BulkUpdate(updateData, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return -1;
      }
    }

    public static async Task<int> BulkDeleteAsync<TEntity>(IDbConnection db,
        Action<IConditionalBulkSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (await Sync.LockAsync())
      {
        try
        {
          return await db.BulkDeleteAsync(statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return -1;
      }
    }

    public static IEnumerable<TEntity> Find<TEntity>(IDbConnection db,
        Action<IRangedBatchSelectSqlSqlStatementOptionsOptionsBuilder<TEntity>> statementOptions = null)
    {
    //  using (Sync.Lock())
      {
        try
        {
          return db.Find(statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return new List<TEntity>();
      }
    }

    public static async Task<IEnumerable<TEntity>> FindAsync<TEntity>(IDbConnection db,
        Action<IRangedBatchSelectSqlSqlStatementOptionsOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (await Sync.LockAsync())
      {
        try
        {
          return await db.FindAsync(statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return new List<TEntity>();
      }
    }

    public static bool Update<TEntity>(IDbConnection db, TEntity entityToUpdate,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
    //  using (Sync.Lock())
      {
        try
        {
          return db.Update(entityToUpdate, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return false;
      }
    }

    public static async Task<bool> UpdateAsync<TEntity>(IDbConnection db, TEntity entityToUpdate,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (await Sync.LockAsync())
      {
        try
        {
          return await db.UpdateAsync(entityToUpdate, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return false;
      }
    }

    public static bool Delete<TEntity>(IDbConnection db, TEntity entityToDelete,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (Sync.Lock())
      {
        try
        {
          return db.Delete(entityToDelete, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return false;
      }
    }

    public static async Task<bool> DeleteAsync<TEntity>(IDbConnection db, TEntity entityToDelete,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
    //  using (await Sync.LockAsync())
      {
        try
        {
          return await db.DeleteAsync(entityToDelete, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }

        return false;
      }
    }

    public static void Insert<TEntity>(IDbConnection db, TEntity entityToInsert,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (Sync.Lock())
      {
        try
        {
          db.InsertAsync(entityToInsert, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }
      }
    }

    public static async Task InsertAsync<TEntity>(IDbConnection db, TEntity entityToInsert,
        Action<IStandardSqlStatementOptionsBuilder<TEntity>> statementOptions = null)
    {
     // using (await Sync.LockAsync())
      {
        try
        {
          await db.InsertAsync(entityToInsert, statementOptions);
        }
        catch (Exception e)
        {
          Logger.Error($"DBError, {e}");
        }
      }
    }
  }
}