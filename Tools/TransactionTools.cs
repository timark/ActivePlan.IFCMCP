using System.ComponentModel;
using IfcMcpServer.Models;
using IfcMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace IfcMcpServer.Tools;

[McpServerToolType]
public sealed class TransactionTools(IfcService ifc, ILogger<TransactionTools> logger)
{
    [McpServerTool(Name = "begin_transaction")]
    [Description("Start a new transaction. All write operations require an active transaction. Fails if a transaction is already open.")]
    public Task<TransactionResult> BeginTransaction()
    {
        logger.LogDebug("begin_transaction");
        ifc.BeginTransaction();
        return Task.FromResult(new TransactionResult("Transaction started"));
    }

    [McpServerTool(Name = "commit_transaction")]
    [Description("Commit the active transaction, persisting all changes in memory. Call save_model or save_model_as to write to disk.")]
    public Task<TransactionResult> CommitTransaction()
    {
        logger.LogDebug("commit_transaction");
        ifc.CommitTransaction();
        return Task.FromResult(new TransactionResult("Transaction committed"));
    }

    [McpServerTool(Name = "rollback_transaction")]
    [Description("Rollback the active transaction, discarding all changes made since begin_transaction.")]
    public Task<TransactionResult> RollbackTransaction()
    {
        logger.LogDebug("rollback_transaction");
        ifc.RollbackTransaction();
        return Task.FromResult(new TransactionResult("Transaction rolled back"));
    }
}
