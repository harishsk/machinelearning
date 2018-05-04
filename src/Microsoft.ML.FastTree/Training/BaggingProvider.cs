// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.ML.Runtime.FastTree.Internal
{
    public class BaggingProvider
    {
        protected Dataset CompleteTrainingSet;
        protected DocumentPartitioning CurrentTrainPartition;
        protected DocumentPartitioning CurrentOutOfBagPartition;

        protected Random RndGenerator;
        protected int MaxLeaves;
        protected double TrainFraction;

        public BaggingProvider(Dataset completeTrainingSet, int maxLeaves, int randomSeed, double trainFraction)
        {
            CompleteTrainingSet = completeTrainingSet;
            MaxLeaves = maxLeaves;
            RndGenerator = new Random(randomSeed);
            TrainFraction = trainFraction;
            GenerateNewBag();
        }

        public virtual void GenerateNewBag()
        {
            int[] trainDocs = new int[CompleteTrainingSet.NumDocs];
            int[] outOfBagDocs = new int[CompleteTrainingSet.NumDocs];
            int trainSize = 0;
            int outOfBagSize = 0;

            for (int i = 0; i < CompleteTrainingSet.NumQueries; i++)
            {
                int begin = CompleteTrainingSet.Boundaries[i];
                int numDocuments = CompleteTrainingSet.Boundaries[i + 1] - begin;
                for (int d = 0; d < numDocuments; d++)
                {
                    if (RndGenerator.NextDouble() < TrainFraction)
                    {
                        trainDocs[trainSize] = begin + d;
                        trainSize++;
                    }
                    else
                    {
                        outOfBagDocs[outOfBagSize] = begin + d;
                        outOfBagSize++;
                    }
                }
            }

            CurrentTrainPartition = new DocumentPartitioning(trainDocs, trainSize, MaxLeaves);
            CurrentOutOfBagPartition = new DocumentPartitioning(outOfBagDocs, outOfBagSize, MaxLeaves);
            CurrentTrainPartition.Initialize();
            CurrentOutOfBagPartition.Initialize();
        }

        public DocumentPartitioning GetCurrentTrainingPartition()
        {
            return CurrentTrainPartition;
        }

        public DocumentPartitioning GetCurrentOutOfBagPartition()
        {
            return CurrentOutOfBagPartition;
        }

        public int GetBagCount(int numTrees, int bagSize)
        {
            return numTrees / bagSize;
        }

        // Divides output values of leaves to bag count.
        // This brings back the final scores generated by model on a same
        // range as when we didn't use bagging
        public void ScaleEnsembleLeaves(int numTrees, int bagSize, Ensemble ensemble)
        {
            int bagCount = GetBagCount(numTrees, bagSize);
            for (int t = 0; t < ensemble.NumTrees; t++)
            {
                RegressionTree tree = ensemble.GetTreeAt(t);
                tree.ScaleOutputsBy(1.0 / bagCount);
            }
        }
    }

    //REVIEW: Should FastTree binary application have instances bagging or query bagging?
    public class RankingBaggingProvider : BaggingProvider
    {
        public RankingBaggingProvider(Dataset completeTrainingSet, int maxLeaves, int randomSeed, double trainFraction) :
            base(completeTrainingSet, maxLeaves, randomSeed, trainFraction)
        {
        }

        public override void GenerateNewBag()
        {
            int[] trainDocs = new int[CompleteTrainingSet.NumDocs];
            int[] outOfBagDocs = new int[CompleteTrainingSet.NumDocs];
            int trainSize = 0;
            int outOfBagSize = 0;

            int[] tmpTrainQueryIndices = new int[CompleteTrainingSet.NumQueries];
            bool[] selectedTrainQueries = new bool[CompleteTrainingSet.NumQueries];

            int qIdx = 0;
            for (int i = 0; i < CompleteTrainingSet.NumQueries; i++)
            {
                int begin = CompleteTrainingSet.Boundaries[i];
                int numDocuments = CompleteTrainingSet.Boundaries[i + 1] - begin;

                if (RndGenerator.NextDouble() < TrainFraction)
                {
                    for (int d = 0; d < numDocuments; d++)
                    {
                        trainDocs[trainSize] = begin + d;
                        trainSize++;
                    }
                    tmpTrainQueryIndices[qIdx] = i;
                    qIdx++;
                    selectedTrainQueries[i] = true;
                }
            }

            int outOfBagQueriesCount = CompleteTrainingSet.NumQueries - qIdx;

            var currentTrainQueryIndices = new int[CompleteTrainingSet.NumQueries - outOfBagQueriesCount];
            Array.Copy(tmpTrainQueryIndices, currentTrainQueryIndices, currentTrainQueryIndices.Length);

            var currentOutOfBagQueryIndices = new int[outOfBagQueriesCount];
            int outOfBagQIdx = 0;
            for (int q = 0; q < CompleteTrainingSet.NumQueries; q++)
            {
                if (!selectedTrainQueries[q])
                {
                    int begin = CompleteTrainingSet.Boundaries[q];
                    int numDocuments = CompleteTrainingSet.Boundaries[q + 1] - begin;

                    for (int d = 0; d < numDocuments; d++)
                    {
                        outOfBagDocs[outOfBagSize] = begin + d;
                        outOfBagSize++;
                    }
                    currentOutOfBagQueryIndices[outOfBagQIdx] = q;
                    outOfBagQIdx++;
                }
            }

            CurrentTrainPartition = new DocumentPartitioning(trainDocs, trainSize, MaxLeaves);
            CurrentOutOfBagPartition = new DocumentPartitioning(outOfBagDocs, outOfBagSize, MaxLeaves);
            CurrentTrainPartition.Initialize();
            CurrentOutOfBagPartition.Initialize();
        }
    }
}
