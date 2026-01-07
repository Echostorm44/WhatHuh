using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace WhatHuh.Core.Services.SileroVad;

public class SileroVadOnnxModel : IDisposable
{
    private readonly InferenceSession Session;
    private float[][][] State;
    private float[][] Context;
    private int LastSr = 0;
    private int LastBatchSize = 0;
    private static readonly List<int> SampleRates = [8000, 16000];

    public SileroVadOnnxModel(string modelPath)
    {
        var sessionOptions = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1,
            EnableCpuMemArena = true
        };

        Session = new InferenceSession(modelPath, sessionOptions);
        State = [];
        Context = [];
        ResetStates();
    }

    public void ResetStates()
    {
        State = new float[2][][];
        State[0] = [new float[128]];
        State[1] = [new float[128]];
        Context = [];
        LastSr = 0;
        LastBatchSize = 0;
    }

    public void Dispose()
    {
        Session.Dispose();
        GC.SuppressFinalize(this);
    }

    private static (float[][] X, int Sr) ValidateInput(float[][] x, int sr)
    {
        if (x.Length == 1)
        {
            x = [x[0]];
        }
        if (x.Length > 2)
        {
            throw new ArgumentException($"Incorrect audio data dimension: {x[0].Length}");
        }

        if (sr != 16000 && (sr % 16000 == 0))
        {
            int step = sr / 16000;
            float[][] reducedX = new float[x.Length][];

            for (int i = 0; i < x.Length; i++)
            {
                float[] current = x[i];
                float[] newArr = new float[(current.Length + step - 1) / step];

                for (int j = 0, index = 0; j < current.Length; j += step, index++)
                {
                    newArr[index] = current[j];
                }

                reducedX[i] = newArr;
            }

            x = reducedX;
            sr = 16000;
        }

        if (!SampleRates.Contains(sr))
        {
            throw new ArgumentException($"Only supports sample rates {string.Join(", ", SampleRates)} (or multiples of 16000)");
        }

        if (((float)sr) / x[0].Length > 31.25)
        {
            throw new ArgumentException("Input audio is too short");
        }

        return (x, sr);
    }

    private static float[][] Concatenate(float[][] a, float[][] b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException("The number of rows in both arrays must be the same.");
        }

        int rows = a.Length;
        int colsA = a[0].Length;
        int colsB = b[0].Length;
        float[][] result = new float[rows][];

        for (int i = 0; i < rows; i++)
        {
            result[i] = new float[colsA + colsB];
            Array.Copy(a[i], 0, result[i], 0, colsA);
            Array.Copy(b[i], 0, result[i], colsA, colsB);
        }

        return result;
    }

    private static float[][] GetLastColumns(float[][] array, int contextSize)
    {
        int rows = array.Length;
        int cols = array[0].Length;

        if (contextSize > cols)
        {
            throw new ArgumentException("contextSize cannot be greater than the number of columns in the array.");
        }

        float[][] result = new float[rows][];

        for (int i = 0; i < rows; i++)
        {
            result[i] = new float[contextSize];
            Array.Copy(array[i], cols - contextSize, result[i], 0, contextSize);
        }

        return result;
    }

    public float[] Call(float[][] x, int sr)
    {
        var validation = ValidateInput(x, sr);
        x = validation.X;
        sr = validation.Sr;
        int numberSamples = sr == 16000 ? 512 : 256;

        if (x[0].Length != numberSamples)
        {
            throw new ArgumentException($"Provided number of samples is {x[0].Length} (Supported values: 256 for 8000 sample rate, 512 for 16000)");
        }

        int batchSize = x.Length;
        int contextSize = sr == 16000 ? 64 : 32;

        if (LastBatchSize == 0)
        {
            ResetStates();
        }
        if (LastSr != 0 && LastSr != sr)
        {
            ResetStates();
        }
        if (LastBatchSize != 0 && LastBatchSize != batchSize)
        {
            ResetStates();
        }

        if (Context.Length == 0)
        {
            Context = new float[batchSize][];
            for (int i = 0; i < batchSize; i++)
            {
                Context[i] = new float[contextSize];
            }
        }

        x = Concatenate(Context, x);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", new DenseTensor<float>(x.SelectMany(a => a).ToArray(), [x.Length, x[0].Length])),
            NamedOnnxValue.CreateFromTensor("sr", new DenseTensor<long>(new long[] { sr }, new int[] { 1 })),
            NamedOnnxValue.CreateFromTensor("state", new DenseTensor<float>(State.SelectMany(a => a.SelectMany(b => b)).ToArray(), [State.Length, State[0].Length, State[0][0].Length]))
        };

        using var outputs = Session.Run(inputs);
        var output = outputs.First(o => o.Name == "output").AsTensor<float>();
        var newState = outputs.First(o => o.Name == "stateN").AsTensor<float>();

        Context = GetLastColumns(x, contextSize);
        LastSr = sr;
        LastBatchSize = batchSize;

        State = new float[newState.Dimensions[0]][][];
        for (int i = 0; i < newState.Dimensions[0]; i++)
        {
            State[i] = new float[newState.Dimensions[1]][];
            for (int j = 0; j < newState.Dimensions[1]; j++)
            {
                State[i][j] = new float[newState.Dimensions[2]];
                for (int k = 0; k < newState.Dimensions[2]; k++)
                {
                    State[i][j][k] = newState[i, j, k];
                }
            }
        }

        return [.. output];
    }
}
