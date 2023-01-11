using System;
using System.Collections.Generic;

namespace Library.Common
{

  public enum FailureReason { Success, NotFound, ApplicationError, SystemError, Constraint, AccessDenied, Disconnected, Exception, AlreadyExists, Timeout, Retry }

  public class ResultWrapper<ReturnType>
  {
    public ResultWrapper(ReturnType result)
    {
      Result = result;
      IsOK = true;
      FailureReason = FailureReason.Success;
      Exception = null;
    }

    public ResultWrapper()
    {
      Result = default(ReturnType);
      IsOK = true;
      FailureReason = FailureReason.Success;
      Exception = null;
    }

    public ResultWrapper(ReturnType result, FailureReason failureReason)
    {
      Result = result;
      IsOK = false;
      FailureReason = failureReason;
      Exception = null;
    }

    public ResultWrapper(ReturnType result, Exception exception)
    {
      Result = result;
      IsOK = false;
      FailureReason = FailureReason.Exception;
      Exception = exception;
    }

    public ResultWrapper(ReturnType result, Exception exception, FailureReason failureReason)
    {
      Result = result;
      IsOK = false;
      FailureReason = failureReason;
      Exception = exception;
    }

    public ResultWrapper(Exception exception, FailureReason failureReason)
    {
      Result = default(ReturnType);
      IsOK = false;
      FailureReason = failureReason;
      Exception = exception;
    }

    public ResultWrapper(FailureReason failureReason)
    {
      Result = default(ReturnType);
      IsOK = false;
      FailureReason = failureReason;
      Exception = null;
    }

    public ResultWrapper(Exception exception)
    {
      Result = default(ReturnType);
      IsOK = false;
      FailureReason = FailureReason.Exception;
      Exception = exception;
    }

    public ReturnType Result { get; protected set; }
    public bool IsOK { get; protected set; }
    public FailureReason FailureReason { get; protected set; }
    public Exception Exception { get; protected set; }

    public override string ToString()
    {
      return (IsOK ? "OK" : ("Failure: " + FailureReason.ToString())) + " {" + (Result?.ToString() ?? "NULL") + "}";
    }
  }

  public class ResultReason
  {
    public ResultReason(object result, string reason)
    {
      Result = result;
      Reason = reason;
      NestedResults = new List<ResultReason>();
    }

    public ResultReason(object result, string reason, IEnumerable<ResultReason> nestedResults)
    {
      Result = result;
      Reason = reason;
      NestedResults = new List<ResultReason>(nestedResults);
    }

    public void AddNestedResult(ResultReason nestedResult) => NestedResults.Add(nestedResult);

    public T GetResult<T>() => (T)Result;

    public object Result { get; protected set; }
    public string Reason { get; protected set; }
    public List<ResultReason> NestedResults { get; protected set; }
  }
}