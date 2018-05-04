﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Api;
using Microsoft.ML.Runtime.Data;

namespace Microsoft.ML.Models
{
    /// <summary>
    /// This class contains the overall metrics computed by multi-class classification evaluators.
    /// </summary>
    public sealed class ClassificationMetrics
    {
        private ClassificationMetrics()
        {
        }

        internal static ClassificationMetrics FromMetrics(IHostEnvironment env, IDataView overallMetrics, IDataView confusionMatrix)
        {
            Contracts.AssertValue(env);
            env.AssertValue(overallMetrics);
            env.AssertValue(confusionMatrix);

            var metricsEnumerable = overallMetrics.AsEnumerable<SerializationClass>(env, true, ignoreMissingColumns: true);
            var enumerator = metricsEnumerable.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                throw env.Except("The overall RegressionMetrics didn't have any rows.");
            }

            SerializationClass metrics = enumerator.Current;

            if (enumerator.MoveNext())
            {
                throw env.Except("The overall RegressionMetrics contained more than 1 row.");
            }

            return new ClassificationMetrics()
            {
                AccuracyMicro = metrics.AccuracyMicro,
                AccuracyMacro = metrics.AccuracyMacro,
                LogLoss = metrics.LogLoss,
                LogLossReduction = metrics.LogLossReduction,
                TopKAccuracy = metrics.TopKAccuracy,
                PerClassLogLoss = metrics.PerClassLogLoss,
                ConfusionMatrix = ConfusionMatrix.Create(env, confusionMatrix)
            };
        }

        /// <summary>
        /// Gets the micro-average accuracy of the model.
        /// </summary>
        /// <remarks>
        /// The micro-average is the fraction of instances predicted correctly.
        /// 
        /// The micro-average metric weighs each class according to the number of instances that belong
        /// to it in the dataset.
        /// </remarks>
        public double AccuracyMicro { get; private set; }

        /// <summary>
        /// Gets the macro-average accuracy of the model.
        /// </summary>
        /// <remarks>
        /// The macro-average is computed by taking the average over all the classes of the fraction
        /// of correct predictions in this class (the number of correctly predicted instances in the class,
        /// divided by the total number of instances in the class).
        /// 
        /// The macro-average metric gives the same weight to each class, no matter how many instances from
        /// that class the dataset contains.
        /// </remarks>
        public double AccuracyMacro { get; private set; }

        /// <summary>
        /// Gets the average log-loss of the classifier.
        /// </summary>
        /// <remarks>
        /// The log-loss metric, is computed as follows:
        /// LL = - (1/m) * sum( log(p[i]))
        /// where m is the number of instances in the test set.
        /// p[i] is the probability returned by the classifier if the instance belongs to class 1,
        /// and 1 minus the probability returned by the classifier if the instance belongs to class 0.
        /// </remarks>
        public double LogLoss { get; private set; }

        /// <summary>
        /// Gets the log-loss reduction (also known as relative log-loss, or reduction in information gain - RIG)
        /// of the classifier.
        /// </summary>
        /// <remarks>
        /// The log-loss reduction is scaled relative to a classifier that predicts the prior for every example:
        /// (LL(prior) - LL(classifier)) / LL(prior)
        /// This metric can be interpreted as the advantage of the classifier over a random prediction.
        /// E.g., if the RIG equals 20, it can be interpreted as "the probability of a correct prediction is
        /// 20% better than random guessing".
        /// </remarks>
        public double LogLossReduction { get; private set; }

        /// <summary>
        /// If <see cref="ClassificationEvaluator.OutputTopKAcc"/> was specified on the
        /// evaluator to be k, then TopKAccuracy is the relative number of examples where
        /// the true label is one of the top k predicted labels by the predictor.
        /// </summary>
        public double TopKAccuracy { get; private set; }

        /// <summary>
        /// Gets the log-loss of the classifier for each class.
        /// </summary>
        /// <remarks>
        /// The log-loss metric, is computed as follows:
        /// LL = - (1/m) * sum( log(p[i]))
        /// where m is the number of instances in the test set.
        /// p[i] is the probability returned by the classifier if the instance belongs to the class,
        /// and 1 minus the probability returned by the classifier if the instance does not belong to the class.
        /// </remarks>
        public double[] PerClassLogLoss { get; private set; }

        /// <summary>
        /// Gets the confusion matrix, or error matrix, of the classifier.
        /// </summary>
        public ConfusionMatrix ConfusionMatrix { get; private set; }

        /// <summary>
        /// This class contains the public fields necessary to deserialize from IDataView.
        /// </summary>
        private class SerializationClass
        {
#pragma warning disable 649 // never assigned
            [ColumnName(MultiClassClassifierEvaluator.AccuracyMicro)]
            public double AccuracyMicro;

            [ColumnName(MultiClassClassifierEvaluator.AccuracyMacro)]
            public double AccuracyMacro;

            [ColumnName(MultiClassClassifierEvaluator.LogLoss)]
            public double LogLoss;

            [ColumnName(MultiClassClassifierEvaluator.LogLossReduction)]
            public double LogLossReduction;

            [ColumnName(MultiClassClassifierEvaluator.TopKAccuracy)]
            public double TopKAccuracy;

            [ColumnName(MultiClassClassifierEvaluator.PerClassLogLoss)]
            public double[] PerClassLogLoss;
#pragma warning restore 649 // never assigned
        }
    }
}
