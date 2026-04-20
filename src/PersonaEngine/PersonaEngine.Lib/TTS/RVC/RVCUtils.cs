using Microsoft.ML.OnnxRuntime.Tensors;

namespace PersonaEngine.Lib.TTS.RVC;

internal static class RVCUtils
{
    public static DenseTensor<float> RepeatTensor(DenseTensor<float> tensor, int times)
    {
        // Create output tensor with expanded dimensions
        var inputDims = tensor.Dimensions;
        var outputDims = inputDims.ToArray();
        outputDims[2] *= times;
        var result = new DenseTensor<float>(outputDims);

        // Cache dimensions for faster access
        var dim0 = inputDims[0];
        var dim1 = inputDims[1];
        var dim2 = inputDims[2];
        var newDim2 = dim2 * times;

        // Compute strides for efficient indexing
        var inputStride1 = dim2;
        var inputStride0 = dim1 * inputStride1;

        var outputStride1 = newDim2;
        var outputStride0 = dim1 * outputStride1;

        // Use regular for-loop with thread partitioning instead of Parallel.For with lambda
        var totalBatches = dim0;
        var numThreads = Environment.ProcessorCount;
        var batchesPerThread = (totalBatches + numThreads - 1) / numThreads;

        Parallel.For(
            0,
            numThreads,
            threadIndex =>
            {
                // Calculate the range for this thread
                var startBatch = threadIndex * batchesPerThread;
                var endBatch = Math.Min(startBatch + batchesPerThread, totalBatches);

                // Process assigned batches without capturing Span
                for (var i = startBatch; i < endBatch; i++)
                {
                    // Calculate base offsets for each dimension
                    var inputBaseI = i * inputStride0;
                    var outputBaseI = i * outputStride0;

                    for (var j = 0; j < dim1; j++)
                    {
                        var inputBaseJ = inputBaseI + j * inputStride1;
                        var outputBaseJ = outputBaseI + j * outputStride1;

                        for (var k = 0; k < dim2; k++)
                        {
                            // Get input value using direct indexers
                            var value = tensor.GetValue(inputBaseJ + k);
                            var outputStartIdx = outputBaseJ + k * times;

                            // Set output values in a loop
                            for (var t = 0; t < times; t++)
                            {
                                result.SetValue(outputStartIdx + t, value);
                            }
                        }
                    }
                }
            }
        );

        return result;
    }

    public static DenseTensor<T> Transpose<T>(Tensor<T> tensor, params int[] perm)
    {
        // Create output tensor with transposed dimensions
        var outputDims = new int[perm.Length];
        for (var i = 0; i < perm.Length; i++)
        {
            outputDims[i] = tensor.Dimensions[perm[i]];
        }

        var result = new DenseTensor<T>(outputDims);

        // Optimize for the specific (0,2,1) permutation used in the codebase
        if (
            tensor.Dimensions.Length == 3
            && perm.Length == 3
            && perm[0] == 0
            && perm[1] == 2
            && perm[2] == 1
        )
        {
            TransposeOptimized021(tensor, result);

            return result;
        }

        var rank = tensor.Dimensions.Length;

        // Precompute input strides for faster coordinate calculation
        var inputStrides = new int[rank];
        inputStrides[rank - 1] = 1;
        for (var i = rank - 2; i >= 0; i--)
        {
            inputStrides[i] = inputStrides[i + 1] * tensor.Dimensions[i + 1];
        }

        // Precompute output strides
        var outputStrides = new int[rank];
        outputStrides[rank - 1] = 1;
        for (var i = rank - 2; i >= 0; i--)
        {
            outputStrides[i] = outputStrides[i + 1] * outputDims[i + 1];
        }

        // Process in chunks for better parallelization
        const int chunkSize = 4096;
        var totalChunks = (tensor.Length + chunkSize - 1) / chunkSize;

        Parallel.For(
            0,
            totalChunks,
            chunkIdx =>
            {
                var startIdx = (int)chunkIdx * chunkSize;
                var endIdx = Math.Min(startIdx + chunkSize, tensor.Length);

                // Allocate coordinate array for this thread
                var coords = new int[rank];

                // Process this chunk
                for (var flatIdx = startIdx; flatIdx < endIdx; flatIdx++)
                {
                    // Convert flat index to coordinates
                    var remaining = flatIdx;
                    for (var dim = 0; dim < rank; dim++)
                    {
                        coords[dim] = remaining / inputStrides[dim];
                        remaining %= inputStrides[dim];
                    }

                    // Compute output index
                    var outputIdx = 0;
                    for (var dim = 0; dim < rank; dim++)
                    {
                        outputIdx += coords[perm[dim]] * outputStrides[dim];
                    }

                    // Transfer the value
                    result.SetValue(outputIdx, tensor.GetValue(flatIdx));
                }
            }
        );

        return result;
    }

    // Highly optimized method for the specific (0,2,1) permutation
    private static void TransposeOptimized021<T>(Tensor<T> input, DenseTensor<T> output)
    {
        var dim0 = input.Dimensions[0];
        var dim1 = input.Dimensions[1];
        var dim2 = input.Dimensions[2];

        // Partitioning for parallel processing
        var numThreads = Environment.ProcessorCount;
        var batchesPerThread = (dim0 + numThreads - 1) / numThreads;

        Parallel.For(
            0,
            numThreads,
            threadIndex =>
            {
                var startBatch = threadIndex * batchesPerThread;
                var endBatch = Math.Min(startBatch + batchesPerThread, dim0);

                // Process batches assigned to this thread
                for (var i = startBatch; i < endBatch; i++)
                {
                    // Compute base offsets for this batch
                    var inputBatchOffset = i * dim1 * dim2;
                    var outputBatchOffset = i * dim2 * dim1;

                    // Cache-friendly block processing
                    const int blockSize = 16; // Tuned for typical CPU cache line size

                    // Process the 2D slice in blocks for better cache locality
                    for (var jBlock = 0; jBlock < dim1; jBlock += blockSize)
                    {
                        for (var kBlock = 0; kBlock < dim2; kBlock += blockSize)
                        {
                            // Determine actual block boundaries
                            var jEnd = Math.Min(jBlock + blockSize, dim1);
                            var kEnd = Math.Min(kBlock + blockSize, dim2);

                            // Process the block
                            for (var j = jBlock; j < jEnd; j++)
                            {
                                var inputRowOffset = inputBatchOffset + j * dim2;

                                for (var k = kBlock; k < kEnd; k++)
                                {
                                    var inputIdx = inputRowOffset + k;
                                    var outputIdx = outputBatchOffset + k * dim1 + j;

                                    // Use element access methods instead of Span
                                    output.SetValue(outputIdx, input.GetValue(inputIdx));
                                }
                            }
                        }
                    }
                }
            }
        );
    }
}
