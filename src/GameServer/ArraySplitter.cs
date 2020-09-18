namespace NeoNetsphere
{
  using System;
  using System.Collections.Generic;

  public class ArraySplitter
  {
    public static List<T[]> Split<T>(T[] array, int maxSize)
    {
      var arrayList = new List<T[]>();
      var readoffset = 0;

      while (readoffset != array.Length)
      {
        var size = array.Length - readoffset;

        if (size > maxSize)
          size = maxSize;

        var data = new T[size];
        Array.Copy(array, readoffset, data, 0, size);
        arrayList.Add(data);
        readoffset += size;
      }

      return arrayList;
    }
  }
}