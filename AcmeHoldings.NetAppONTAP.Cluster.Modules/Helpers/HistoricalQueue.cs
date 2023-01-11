using System;

namespace AcmeHoldings.Library.Common
{
  public class HistoricalQueue<T>
  {
    private T[] _Items;
    private DateTime[] _ItemTimestamps;
    private int _Size;
    public HistoricalQueue(int MaxDepth)
    {
      _Items = new T[MaxDepth];
      _ItemTimestamps = new DateTime[MaxDepth];
      _Size = 0;
    }

    public int Count
    {
      get { return _Size; }
    }

    public void Enqueue(T inputData, DateTime dataTimestamp)
    {
      // shift items
      for (int i = _Size; i >= 1; i--)
      {
        if (i == _Items.Length)
          continue; // waste the oldest result
        _Items[i] = _Items[i - 1];
        _ItemTimestamps[i] = _ItemTimestamps[i - 1];
      }
      // insert item
      _Items[0] = inputData;
      _ItemTimestamps[0] = dataTimestamp;
      // finally, when all dome
      if (_Size < _Items.Length)
        _Size++;
    }

    public void Enqueue(T inputData)
    {
      Enqueue(inputData, DateTime.Now);
    }

    public T MostRecent
    {
      get { return _Items[0]; }
    }

    public DateTime MostRecentTimestamp
    {
      get { return _ItemTimestamps[0]; }
    }

    public T this[int idx]
    {
      get { return _Items[idx]; }
      set { _Items[idx] = value; }
    }

    public DateTime TimestampAt(int idx)
    {
      return _ItemTimestamps[idx];
    }
  }
}

