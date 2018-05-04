// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.Runtime;
using Microsoft.ML.Runtime.Command;
using Microsoft.ML.Runtime.CommandLine;
using Microsoft.ML.Runtime.Data;
using Microsoft.ML.Runtime.Internal.Calibration;
using Microsoft.ML.Runtime.Internal.Utilities;

[assembly: LoadableClass(typeof(CrossValidationCommand), typeof(CrossValidationCommand.Arguments), typeof(SignatureCommand),
    "Cross Validation", CrossValidationCommand.LoadName)]

namespace Microsoft.ML.Runtime.Data
{
    public sealed class CrossValidationCommand : DataCommand.ImplBase<CrossValidationCommand.Arguments>
    {
        // REVIEW: We need a way to specify different data sets, not just LabeledExamples.
        public sealed class Arguments : DataCommand.ArgumentsBase
        {
            [Argument(ArgumentType.Multiple, HelpText = "Trainer to use", ShortName = "tr")]
            public SubComponent<ITrainer, SignatureTrainer> Trainer = new SubComponent<ITrainer, SignatureTrainer>("AveragedPerceptron");

            [Argument(ArgumentType.Multiple, HelpText = "Scorer to use", NullName = "<Auto>", SortOrder = 101)]
            public SubComponent<IDataScorerTransform, SignatureDataScorer> Scorer;

            [Argument(ArgumentType.Multiple, HelpText = "Evaluator to use", ShortName = "eval", NullName = "<Auto>", SortOrder = 102)]
            public SubComponent<IMamlEvaluator, SignatureMamlEvaluator> Evaluator;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Results summary filename", ShortName = "sf")]
            public string SummaryFilename;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Column to use for features", ShortName = "feat", SortOrder = 2)]
            public string FeatureColumn = DefaultColumnNames.Features;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Column to use for labels", ShortName = "lab", SortOrder = 3)]
            public string LabelColumn = DefaultColumnNames.Label;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Column to use for example weight", ShortName = "weight", SortOrder = 4)]
            public string WeightColumn = DefaultColumnNames.Weight;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Column to use for grouping", ShortName = "group", SortOrder = 5)]
            public string GroupColumn = DefaultColumnNames.GroupId;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Name column name", ShortName = "name", SortOrder = 6)]
            public string NameColumn = DefaultColumnNames.Name;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Column to use for stratification", ShortName = "strat", SortOrder = 7)]
            public string StratificationColumn;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Columns with custom kinds declared through key assignments, e.g., col[Kind]=Name to assign column named 'Name' kind 'Kind'", ShortName = "col", SortOrder = 10)]
            public KeyValuePair<string, string>[] CustomColumn;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Number of folds in k-fold cross-validation", ShortName = "k")]
            public int NumFolds = 2;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Use threads", ShortName = "threads")]
            public bool UseThreads = true;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Normalize option for the feature column", ShortName = "norm")]
            public NormalizeOption NormalizeFeatures = NormalizeOption.Auto;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Whether we should cache input training data", ShortName = "cache")]
            public bool? CacheData;

            [Argument(ArgumentType.Multiple, HelpText = "Transforms to apply prior to splitting the data into folds", ShortName = "prexf")]
            public KeyValuePair<string, SubComponent<IDataTransform, SignatureDataTransform>>[] PreTransform;

            [Argument(ArgumentType.AtMostOnce, IsInputFileName = true, HelpText = "The validation data file", ShortName = "valid")]
            public string ValidationFile;

            [Argument(ArgumentType.Multiple, HelpText = "Output calibrator", ShortName = "cali", NullName = "<None>")]
            public SubComponent<ICalibratorTrainer, SignatureCalibrator> Calibrator = new SubComponent<ICalibratorTrainer, SignatureCalibrator>("PlattCalibration");

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Number of instances to train the calibrator", ShortName = "numcali")]
            public int MaxCalibrationExamples = 1000000000;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "File to save per-instance predictions and metrics to",
                ShortName = "dout")]
            public string OutputDataFile;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Print the run/fold index in per-instance output", ShortName = "opf")]
            public bool OutputExampleFoldIndex = false;

            [Argument(ArgumentType.AtMostOnce, HelpText = "Whether we should collate metrics or store them in per-folds files", ShortName = "collate")]
            public bool CollateMetrics = true;

            [Argument(ArgumentType.LastOccurenceWins, HelpText = "Whether we should load predictor from input model and use it as the initial model state", ShortName = "cont")]
            public bool ContinueTrain;
        }

        private const string RegistrationName = nameof(CrossValidationCommand);
        private readonly ComponentCatalog.LoadableClassInfo _info;
        public const string LoadName = "CV";

        public CrossValidationCommand(IHostEnvironment env, Arguments args)
            : base(env, args, RegistrationName)
        {
            Host.CheckUserArg(Args.NumFolds >= 2, nameof(Args.NumFolds), "Number of folds must be greater than or equal to 2.");
            _info = TrainUtils.CheckTrainer(Host, args.Trainer, args.DataFile);
            Utils.CheckOptionalUserDirectory(Args.SummaryFilename, nameof(Args.SummaryFilename));
            Utils.CheckOptionalUserDirectory(Args.OutputDataFile, nameof(Args.OutputDataFile));
        }

        // This is for "forking" the host environment.
        private CrossValidationCommand(CrossValidationCommand impl)
            : base(impl, RegistrationName)
        {
            _info = impl._info;
        }

        public override void Run()
        {
            using (var ch = Host.Start(LoadName))
            using (var server = InitServer(ch))
            {
                var settings = CmdParser.GetSettings(ch, Args, new Arguments());
                string cmd = string.Format("maml.exe {0} {1}", LoadName, settings);
                ch.Info(cmd);

                SendTelemetry(Host);

                using (new TimerScope(Host, ch))
                {
                    RunCore(ch, cmd);
                }

                ch.Done();
            }
        }

        protected override void SendTelemetryCore(IPipe<TelemetryMessage> pipe)
        {
            SendTelemetryComponent(pipe, Args.Trainer);
            base.SendTelemetryCore(pipe);
        }

        private void RunCore(IChannel ch, string cmd)
        {
            Host.AssertValue(ch);

            IPredictor inputPredictor = null;
            if (Args.ContinueTrain && !TrainUtils.TryLoadPredictor(ch, Host, Args.InputModelFile, out inputPredictor))
                ch.Warning("No input model file specified or model file did not contain a predictor. The model state cannot be initialized.");

            ch.Trace("Constructing data pipeline");
            IDataLoader loader = CreateRawLoader();

            // If the per-instance results are requested and there is no name column, add a GenerateNumberTransform.
            var preXf = Args.PreTransform;
            if (!string.IsNullOrEmpty(Args.OutputDataFile))
            {
                string name = TrainUtils.MatchNameOrDefaultOrNull(ch, loader.Schema, nameof(Args.NameColumn), Args.NameColumn, DefaultColumnNames.Name);
                if (name == null)
                {
                    var args = new GenerateNumberTransform.Arguments();
                    args.Column = new[] { new GenerateNumberTransform.Column() { Name = DefaultColumnNames.Name }, };
                    args.UseCounter = true;
                    var options = CmdParser.GetSettings(ch, args, new GenerateNumberTransform.Arguments());
                    preXf = preXf.Concat(
                        new[]
                        {
                                new KeyValuePair<string, SubComponent<IDataTransform, SignatureDataTransform>>(
                                    "", new SubComponent<IDataTransform, SignatureDataTransform>(
                                        GenerateNumberTransform.LoadName, options))
                        }).ToArray();
                }
            }
            loader = CompositeDataLoader.Create(Host, loader, preXf);

            ch.Trace("Binding label and features columns");

            IDataView pipe = loader;
            var stratificationColumn = GetSplitColumn(ch, loader, ref pipe);
            var scorer = Args.Scorer;
            var evaluator = Args.Evaluator;

            Func<IDataView> validDataCreator = null;
            if (Args.ValidationFile != null)
            {
                validDataCreator =
                    () =>
                    {
                        // Fork the command.
                        var impl = new CrossValidationCommand(this);
                        return impl.CreateRawLoader(dataFile: Args.ValidationFile);
                    };
            }

            FoldHelper fold = new FoldHelper(Host, RegistrationName, pipe, stratificationColumn,
                Args, CreateRoleMappedData, ApplyAllTransformsToData, scorer, evaluator,
                validDataCreator, ApplyAllTransformsToData, inputPredictor, cmd, loader, !string.IsNullOrEmpty(Args.OutputDataFile));
            var tasks = fold.GetCrossValidationTasks();

            if (!evaluator.IsGood())
                evaluator = EvaluateUtils.GetEvaluatorType(ch, tasks[0].Result.ScoreSchema);
            var eval = evaluator.CreateInstance(Host);

            // Print confusion matrix and fold results for each fold.
            for (int i = 0; i < tasks.Length; i++)
            {
                var dict = tasks[i].Result.Metrics;
                MetricWriter.PrintWarnings(ch, dict);
                eval.PrintFoldResults(ch, dict);
            }

            // Print the overall results.
            eval.PrintOverallResults(ch, Args.SummaryFilename, tasks.Select(t => t.Result.Metrics).ToArray());
            Dictionary<string, IDataView>[] metricValues = tasks.Select(t => t.Result.Metrics).ToArray();
            SendTelemetryMetric(metricValues);

            // Save the per-instance results.
            if (!string.IsNullOrWhiteSpace(Args.OutputDataFile))
            {
                Func<Task<FoldHelper.FoldResult>, int, IDataView> getPerInstance =
                    (task, i) =>
                    {
                        if (!Args.OutputExampleFoldIndex)
                            return task.Result.PerInstanceResults;

                        // If the fold index is requested, add a column containing it. We use the first column in the data view
                        // as an input column to the LambdaColumnMapper, because it must have an input.
                        var inputColName = task.Result.PerInstanceResults.Schema.GetColumnName(0);
                        var inputColType = task.Result.PerInstanceResults.Schema.GetColumnType(0);
                        return Utils.MarshalInvoke(EvaluateUtils.AddKeyColumn<int>, inputColType.RawType, Host,
                            task.Result.PerInstanceResults, inputColName, MetricKinds.ColumnNames.FoldIndex,
                            inputColType, Args.NumFolds, i + 1, "FoldIndex", default(ValueGetter<VBuffer<DvText>>));
                    };

                var foldDataViews = tasks.Select(getPerInstance).ToArray();
                if (Args.CollateMetrics)
                {
                    var perInst = AppendPerInstanceDataViews(foldDataViews, ch);
                    MetricWriter.SavePerInstance(Host, ch, Args.OutputDataFile, perInst);
                }
                else
                {
                    int i = 0;
                    foreach (var idv in foldDataViews)
                    {
                        MetricWriter.SavePerInstance(Host, ch, ConstructPerFoldName(Args.OutputDataFile, i), idv);
                        i++;
                    }
                }
            }
        }

        private IDataView AppendPerInstanceDataViews(IEnumerable<IDataView> foldDataViews, IChannel ch)
        {
            // Make sure there are no variable size vector columns.
            // This is a dictionary from the column name to its vector size.
            var vectorSizes = new Dictionary<string, int>();
            var firstDvSlotNames = new Dictionary<string, VBuffer<DvText>>();
            var firstDvKeyColumns = new List<string>();
            var firstDvVectorKeyColumns = new List<string>();
            var variableSizeVectorColumnNames = new List<string>();
            var list = new List<IDataView>();
            int dvNumber = 0;
            foreach (var dv in foldDataViews)
            {
                var hidden = new List<int>();
                for (int i = 0; i < dv.Schema.ColumnCount; i++)
                {
                    if (dv.Schema.IsHidden(i))
                    {
                        hidden.Add(i);
                        continue;
                    }

                    var type = dv.Schema.GetColumnType(i);
                    var name = dv.Schema.GetColumnName(i);
                    if (type.IsVector)
                    {
                        if (dvNumber == 0)
                        {
                            if (dv.Schema.HasKeyNames(i, type.ItemType.KeyCount))
                                firstDvVectorKeyColumns.Add(name);
                            // Store the slot names of the 1st idv and use them as baseline.
                            if (dv.Schema.HasSlotNames(i, type.VectorSize))
                            {
                                VBuffer<DvText> slotNames = default(VBuffer<DvText>);
                                dv.Schema.GetMetadata(MetadataUtils.Kinds.SlotNames, i, ref slotNames);
                                firstDvSlotNames.Add(name, slotNames);
                            }
                        }

                        int cachedSize;
                        if (vectorSizes.TryGetValue(name, out cachedSize))
                        {
                            VBuffer<DvText> slotNames;
                            // In the event that no slot names were recorded here, then slotNames will be
                            // the default, length 0 vector.
                            firstDvSlotNames.TryGetValue(name, out slotNames);
                            if (!VerifyVectorColumnsMatch(cachedSize, i, dv, type, ref slotNames))
                                variableSizeVectorColumnNames.Add(name);
                        }
                        else
                            vectorSizes.Add(name, type.VectorSize);
                    }
                    else if (dvNumber == 0 && dv.Schema.HasKeyNames(i, type.KeyCount))
                    {
                        // The label column can be a key. Reconcile the key values, and wrap with a KeyToValue transform.
                        firstDvKeyColumns.Add(name);
                    }
                }
                var idv = dv;
                if (hidden.Count > 0)
                {
                    var args = new ChooseColumnsByIndexTransform.Arguments();
                    args.Drop = true;
                    args.Index = hidden.ToArray();
                    idv = new ChooseColumnsByIndexTransform(Host, args, idv);
                }
                list.Add(idv);
                dvNumber++;
            }

            if (variableSizeVectorColumnNames.Count == 0 && firstDvKeyColumns.Count == 0)
                return AppendRowsDataView.Create(Host, null, list.ToArray());

            var views = list.ToArray();
            foreach (var keyCol in firstDvKeyColumns)
                EvaluateUtils.ReconcileKeyValues(Host, views, keyCol);
            foreach (var vectorKeyCol in firstDvVectorKeyColumns)
                EvaluateUtils.ReconcileVectorKeyValues(Host, views, vectorKeyCol);

            Func<IDataView, int, IDataView> keyToValue =
                (idv, i) =>
                {
                    foreach (var keyCol in firstDvKeyColumns.Concat(firstDvVectorKeyColumns))
                    {
                        idv = new KeyToValueTransform(Host, new KeyToValueTransform.Arguments() { Column = new[] { new KeyToValueTransform.Column() { Name = keyCol }, } }, idv);
                        var hidden = FindHiddenColumns(idv.Schema, keyCol);
                        idv = new ChooseColumnsByIndexTransform(Host, new ChooseColumnsByIndexTransform.Arguments() { Drop = true, Index = hidden.ToArray() }, idv);
                    }
                    return idv;
                };

            Func<IDataView, IDataView> selectDropNonVarLenthCol =
                (idv) =>
                {
                    foreach (var variableSizeVectorColumnName in variableSizeVectorColumnNames)
                    {
                        int index;
                        idv.Schema.TryGetColumnIndex(variableSizeVectorColumnName, out index);
                        var type = idv.Schema.GetColumnType(index);

                        idv = Utils.MarshalInvoke(AddVarLengthColumn<int>, type.ItemType.RawType, Host, idv,
                                 variableSizeVectorColumnName, type);

                        // Drop the old column that does not have variable length.
                        idv = new DropColumnsTransform(Host, new DropColumnsTransform.Arguments() { Column = new[] { variableSizeVectorColumnName } }, idv);
                    }
                    return idv;
                };

            if (variableSizeVectorColumnNames.Count > 0)
                ch.Warning("Detected columns of variable length: {0}. Consider setting collateMetrics- for meaningful per-Folds results.", string.Join(", ", variableSizeVectorColumnNames));
            return AppendRowsDataView.Create(Host, null, views.Select(keyToValue).Select(selectDropNonVarLenthCol).ToArray());
        }

        private static IEnumerable<int> FindHiddenColumns(ISchema schema, string colName)
        {
            for (int i = 0; i < schema.ColumnCount; i++)
            {
                if (schema.IsHidden(i) && schema.GetColumnName(i) == colName)
                    yield return i;
            }
        }

        private static bool VerifyVectorColumnsMatch(int cachedSize, int col, IDataView dv,
            ColumnType type, ref VBuffer<DvText> firstDvSlotNames)
        {
            if (cachedSize != type.VectorSize)
                return false;

            // If we detect mismatch it a sign that slots reshuffling has happened.
            if (dv.Schema.HasSlotNames(col, type.VectorSize))
            {
                // Verify that slots match with slots from 1st idv.
                VBuffer<DvText> currSlotNames = default(VBuffer<DvText>);
                dv.Schema.GetMetadata(MetadataUtils.Kinds.SlotNames, col, ref currSlotNames);

                if (currSlotNames.Length != firstDvSlotNames.Length)
                    return false;
                else
                {
                    var result = true;
                    VBufferUtils.ForEachEitherDefined(ref currSlotNames, ref firstDvSlotNames,
                        (slot, val1, val2) => result = result && DvText.Identical(val1, val2));
                    return result;
                }
            }
            else
            {
                // If we don't have slot names, then the first dataview should not have had slot names either.
                return firstDvSlotNames.Length == 0;
            }
        }

        private static IDataView AddVarLengthColumn<TSrc>(IHostEnvironment env, IDataView idv, string variableSizeVectorColumnName, ColumnType typeSrc)
        {
            return LambdaColumnMapper.Create(env, "ChangeToVarLength", idv, variableSizeVectorColumnName,
                       variableSizeVectorColumnName + "_VarLength", typeSrc, new VectorType(typeSrc.ItemType.AsPrimitive),
                       (ref VBuffer<TSrc> src, ref VBuffer<TSrc> dst) => src.CopyTo(ref dst));
        }

        /// <summary>
        /// Callback from the CV method to apply the transforms from the train data to the test and/or validation data.
        /// </summary>
        private RoleMappedData ApplyAllTransformsToData(IHostEnvironment env, IChannel ch, IDataView dstData,
            RoleMappedData srcData, IDataView marker)
        {
            var pipe = ApplyTransformUtils.ApplyAllTransformsToData(env, srcData.Data, dstData, marker);
            return RoleMappedData.Create(pipe, srcData.Schema.GetColumnRoleNames());
        }

        /// <summary>
        /// Callback from the CV method to apply the transforms to the train data.
        /// </summary>
        private RoleMappedData CreateRoleMappedData(IHostEnvironment env, IChannel ch, IDataView data, ITrainer trainer)
        {
            foreach (var kvp in Args.Transform)
                data = kvp.Value.CreateInstance(env, data);

            var schema = data.Schema;
            string label = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.LabelColumn), Args.LabelColumn, DefaultColumnNames.Label);
            string features = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.FeatureColumn), Args.FeatureColumn, DefaultColumnNames.Features);
            string weight = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.WeightColumn), Args.WeightColumn, DefaultColumnNames.Weight);
            string name = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.NameColumn), Args.NameColumn, DefaultColumnNames.Name);
            string group = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.GroupColumn), Args.GroupColumn, DefaultColumnNames.GroupId);

            TrainUtils.AddNormalizerIfNeeded(env, ch, trainer, ref data, features, Args.NormalizeFeatures);

            // Training pipe and examples.
            var customCols = TrainUtils.CheckAndGenerateCustomColumns(ch, Args.CustomColumn);

            return TrainUtils.CreateExamples(data, label, features, group, weight, name, customCols);
        }

        private string GetSplitColumn(IChannel ch, IDataView input, ref IDataView output)
        {
            // The stratification column and/or group column, if they exist at all, must be present at this point.
            var schema = input.Schema;
            output = input;
            // If no stratification column was specified, but we have a group column of type Single, Double or
            // Key (contiguous) use it.
            string stratificationColumn = null;
            if (!string.IsNullOrWhiteSpace(Args.StratificationColumn))
                stratificationColumn = Args.StratificationColumn;
            else
            {
                string group = TrainUtils.MatchNameOrDefaultOrNull(ch, schema, nameof(Args.GroupColumn), Args.GroupColumn, DefaultColumnNames.GroupId);
                int index;
                if (group != null && schema.TryGetColumnIndex(group, out index))
                {
                    // Check if group column key type with known cardinality.
                    var type = schema.GetColumnType(index);
                    if (type.KeyCount > 0)
                        stratificationColumn = group;
                }
            }

            if (string.IsNullOrEmpty(stratificationColumn))
            {
                stratificationColumn = "StratificationColumn";
                int tmp;
                int inc = 0;
                while (input.Schema.TryGetColumnIndex(stratificationColumn, out tmp))
                    stratificationColumn = string.Format("StratificationColumn_{0:000}", ++inc);
                var keyGenArgs = new GenerateNumberTransform.Arguments();
                var col = new GenerateNumberTransform.Column();
                col.Name = stratificationColumn;
                keyGenArgs.Column = new[] { col };
                output = new GenerateNumberTransform(Host, keyGenArgs, input);
            }
            else
            {
                int col;
                if (!input.Schema.TryGetColumnIndex(stratificationColumn, out col))
                    throw ch.ExceptUserArg(nameof(Arguments.StratificationColumn), "Column '{0}' does not exist", stratificationColumn);
                var type = input.Schema.GetColumnType(col);
                if (!RangeFilter.IsValidRangeFilterColumnType(ch, type))
                {
                    ch.Info("Hashing the stratification column");
                    var origStratCol = stratificationColumn;
                    int tmp;
                    int inc = 0;
                    while (input.Schema.TryGetColumnIndex(stratificationColumn, out tmp))
                        stratificationColumn = string.Format("{0}_{1:000}", origStratCol, ++inc);
                    var hashargs = new HashTransform.Arguments();
                    hashargs.Column = new[] { new HashTransform.Column { Source = origStratCol, Name = stratificationColumn } };
                    hashargs.HashBits = 30;
                    output = new HashTransform(Host, hashargs, input);
                }
            }

            return stratificationColumn;
        }

        private sealed class FoldHelper
        {
            public struct FoldResult
            {
                public readonly Dictionary<string, IDataView> Metrics;
                public readonly ISchema ScoreSchema;
                public readonly IDataView PerInstanceResults;
                public readonly RoleMappedSchema TrainSchema;

                public FoldResult(Dictionary<string, IDataView> metrics, ISchema scoreSchema, IDataView perInstance, RoleMappedSchema trainSchema)
                {
                    Metrics = metrics;
                    ScoreSchema = scoreSchema;
                    PerInstanceResults = perInstance;
                    TrainSchema = trainSchema;
                }
            }

            private readonly IHostEnvironment _env;
            private readonly string _registrationName;
            private readonly IDataView _inputDataView;
            private readonly string _splitColumn;
            private readonly int _numFolds;
            private readonly SubComponent<ITrainer, SignatureTrainer> _trainer;
            private readonly SubComponent<IDataScorerTransform, SignatureDataScorer> _scorer;
            private readonly SubComponent<IMamlEvaluator, SignatureMamlEvaluator> _evaluator;
            private readonly SubComponent<ICalibratorTrainer, SignatureCalibrator> _calibrator;
            private readonly int _maxCalibrationExamples;
            private readonly bool _useThreads;
            private readonly bool? _cacheData;
            private readonly IPredictor _inputPredictor;
            private readonly string _cmd;
            private readonly string _outputModelFile;
            private readonly IDataLoader _loader;
            private readonly bool _savePerInstance;
            private readonly Func<IHostEnvironment, IChannel, IDataView, ITrainer, RoleMappedData> _createExamples;
            private readonly Func<IHostEnvironment, IChannel, IDataView, RoleMappedData, IDataView, RoleMappedData> _applyTransformsToTestData;
            private readonly Func<IDataView> _getValidationDataView;
            private readonly Func<IHostEnvironment, IChannel, IDataView, RoleMappedData, IDataView, RoleMappedData> _applyTransformsToValidationData;

            /// <param name="env">The environment.</param>
            /// <param name="registrationName">The registration name.</param>
            /// <param name="inputDataView">The input data view.</param>
            /// <param name="splitColumn">The column to use for splitting data into folds.</param>
            /// <param name="args">Cross validation arguments.</param>
            /// <param name="createExamples">The delegate to create RoleMappedData</param>
            /// <param name="applyTransformsToTestData">The delegate to apply the transforms from the train pipeline to the test data</param>
            /// <param name="scorer">The scorer</param>
            /// <param name="evaluator">The evaluator</param>
            /// <param name="getValidationDataView">The delegate to create validation data view</param>
            /// <param name="applyTransformsToValidationData">The delegate to apply the transforms from the train pipeline to the validation data</param>
            /// <param name="inputPredictor">The input predictor, for the continue training option</param>
            /// <param name="cmd">The command string.</param>
            /// <param name="loader">Original loader so we can construct correct pipeline for model saving.</param>
            /// <param name="savePerInstance">Whether to produce the per-instance data view.</param>
            /// <returns></returns>
            public FoldHelper(
            IHostEnvironment env,
            string registrationName,
            IDataView inputDataView,
            string splitColumn,
            Arguments args,
            Func<IHostEnvironment, IChannel, IDataView, ITrainer, RoleMappedData> createExamples,
            Func<IHostEnvironment, IChannel, IDataView, RoleMappedData, IDataView, RoleMappedData> applyTransformsToTestData,
            SubComponent<IDataScorerTransform, SignatureDataScorer> scorer,
            SubComponent<IMamlEvaluator, SignatureMamlEvaluator> evaluator,
            Func<IDataView> getValidationDataView = null,
            Func<IHostEnvironment, IChannel, IDataView, RoleMappedData, IDataView, RoleMappedData> applyTransformsToValidationData = null,
            IPredictor inputPredictor = null,
            string cmd = null,
            IDataLoader loader = null,
            bool savePerInstance = false)
            {
                Contracts.CheckValue(env, nameof(env));
                env.CheckNonWhiteSpace(registrationName, nameof(registrationName));
                env.CheckValue(inputDataView, nameof(inputDataView));
                env.CheckValue(splitColumn, nameof(splitColumn));
                env.CheckParam(args.NumFolds > 1, nameof(args.NumFolds));
                env.CheckValue(createExamples, nameof(createExamples));
                env.CheckValue(applyTransformsToTestData, nameof(applyTransformsToTestData));
                env.CheckParam(args.Trainer.IsGood(), nameof(args.Trainer));
                env.CheckValueOrNull(scorer);
                env.CheckValueOrNull(evaluator);
                env.CheckValueOrNull(args.Calibrator);
                env.CheckParam(args.MaxCalibrationExamples > 0, nameof(args.MaxCalibrationExamples));
                env.CheckParam(getValidationDataView == null || applyTransformsToValidationData != null, nameof(applyTransformsToValidationData));
                env.CheckValueOrNull(inputPredictor);
                env.CheckValueOrNull(cmd);
                env.CheckValueOrNull(args.OutputModelFile);
                env.CheckValueOrNull(loader);
                _env = env;
                _registrationName = registrationName;
                _inputDataView = inputDataView;
                _splitColumn = splitColumn;
                _numFolds = args.NumFolds;
                _createExamples = createExamples;
                _applyTransformsToTestData = applyTransformsToTestData;
                _trainer = args.Trainer;
                _scorer = scorer;
                _evaluator = evaluator;
                _calibrator = args.Calibrator;
                _maxCalibrationExamples = args.MaxCalibrationExamples;
                _useThreads = args.UseThreads;
                _cacheData = args.CacheData;
                _getValidationDataView = getValidationDataView;
                _applyTransformsToValidationData = applyTransformsToValidationData;
                _inputPredictor = inputPredictor;
                _cmd = cmd;
                _outputModelFile = args.OutputModelFile;
                _loader = loader;
                _savePerInstance = savePerInstance;
            }

            private IHost GetHost()
            {
                return _env.Register(_registrationName);
            }

            /// <summary>
            /// Creates and runs tasks for each fold of cross validation. The split column is used to split the input data into folds.
            /// There are two cases:
            ///     1. The split column is R4: in this case it assumes that the values are in the interval [0,1] and will split
            ///     this interval into equal width folds. If the values are uniformly distributed it should result in balanced folds.
            ///     2. The split column is key of known cardinality: will split the whole range into equal parts to form folds. If the
            ///     keys are generated by hashing for example, it should result in balanced folds.
            /// </summary>
            /// <returns></returns>
            public Task<FoldResult>[] GetCrossValidationTasks()
            {
                var tasks = new Task<FoldResult>[_numFolds];
                for (int i = 0; i < _numFolds; i++)
                {
                    var fold = i;
                    tasks[i] = new Task<FoldResult>(() =>
                    {
                        return RunFold(fold);
                    });

                    if (_useThreads)
                        tasks[i].Start();
                    else
                        tasks[i].RunSynchronously();
                }
                Task.WaitAll(tasks);
                return tasks;
            }

            private FoldResult RunFold(int fold)
            {
                var host = GetHost();
                host.Assert(0 <= fold && fold <= _numFolds);
                // REVIEW: Make channels buffered in multi-threaded environments.
                using (var ch = host.Start($"Fold {fold}"))
                {
                    ch.Trace("Constructing trainer");
                    ITrainer trainer = _trainer.CreateInstance(host);

                    // Train pipe.
                    var trainFilter = new RangeFilter.Arguments();
                    trainFilter.Column = _splitColumn;
                    trainFilter.Min = (Double)fold / _numFolds;
                    trainFilter.Max = (Double)(fold + 1) / _numFolds;
                    trainFilter.Complement = true;
                    IDataView trainPipe = new RangeFilter(host, trainFilter, _inputDataView);
                    trainPipe = new OpaqueDataView(trainPipe);
                    var trainData = _createExamples(host, ch, trainPipe, trainer);

                    // Test pipe.
                    var testFilter = new RangeFilter.Arguments();
                    testFilter.Column = trainFilter.Column;
                    testFilter.Min = trainFilter.Min;
                    testFilter.Max = trainFilter.Max;
                    ch.Assert(!testFilter.Complement);
                    IDataView testPipe = new RangeFilter(host, testFilter, _inputDataView);
                    testPipe = new OpaqueDataView(testPipe);
                    var testData = _applyTransformsToTestData(host, ch, testPipe, trainData, trainPipe);

                    // Validation pipe and examples.
                    RoleMappedData validData = null;
                    if (_getValidationDataView != null)
                    {
                        ch.Assert(_applyTransformsToValidationData != null);
                        if (!TrainUtils.CanUseValidationData(trainer))
                            ch.Warning("Trainer does not accept validation dataset.");
                        else
                        {
                            ch.Trace("Constructing the validation pipeline");
                            IDataView validLoader = _getValidationDataView();
                            var validPipe = ApplyTransformUtils.ApplyAllTransformsToData(host, _inputDataView, validLoader);
                            validPipe = new OpaqueDataView(validPipe);
                            validData = _applyTransformsToValidationData(host, ch, validPipe, trainData, trainPipe);
                        }
                    }

                    // Train.
                    var predictor = TrainUtils.Train(host, ch, trainData, trainer, _trainer.Kind, validData,
                        _calibrator, _maxCalibrationExamples, _cacheData, _inputPredictor);

                    // Score.
                    ch.Trace("Scoring and evaluating");
                    var bindable = ScoreUtils.GetSchemaBindableMapper(host, predictor, _scorer);
                    ch.AssertValue(bindable);
                    var mapper = bindable.Bind(host, testData.Schema);
                    var scorerComp = _scorer.IsGood() ? _scorer : ScoreUtils.GetScorerComponent(mapper);
                    IDataScorerTransform scorePipe = scorerComp.CreateInstance(host, testData.Data, mapper, trainData.Schema);

                    // Save per-fold model.
                    string modelFileName = ConstructPerFoldName(_outputModelFile, fold);
                    if (modelFileName != null && _loader != null)
                    {
                        using (var file = host.CreateOutputFile(modelFileName))
                        {
                            var rmd = RoleMappedData.Create(
                                CompositeDataLoader.ApplyTransform(host, _loader, null, null,
                                (e, newSource) => ApplyTransformUtils.ApplyAllTransformsToData(e, trainData.Data, newSource)),
                                trainData.Schema.GetColumnRoleNames());
                            TrainUtils.SaveModel(host, ch, file, predictor, rmd, _cmd);
                        }
                    }

                    // Evaluate.
                    var evalComp = _evaluator;
                    if (!evalComp.IsGood())
                        evalComp = EvaluateUtils.GetEvaluatorType(ch, scorePipe.Schema);
                    var eval = evalComp.CreateInstance(host);
                    // Note that this doesn't require the provided columns to exist (because of "Opt").
                    // We don't normally expect the scorer to drop columns, but if it does, we should not require
                    // all the columns in the test pipeline to still be present.
                    var dataEval = RoleMappedData.CreateOpt(scorePipe, testData.Schema.GetColumnRoleNames());

                    var dict = eval.Evaluate(dataEval);
                    IDataView perInstance = null;
                    if (_savePerInstance)
                    {
                        var perInst = eval.GetPerInstanceMetrics(dataEval);
                        var perInstData = RoleMappedData.CreateOpt(perInst, dataEval.Schema.GetColumnRoleNames());
                        perInstance = eval.GetPerInstanceDataViewToSave(perInstData);
                    }
                    ch.Done();
                    return new FoldResult(dict, dataEval.Schema.Schema, perInstance, trainData.Schema);
                }
            }
        }
        /// <summary>
        /// Take path to expected output model file and return path to output model file for specific fold.
        /// Example: \\share\model.zip -> \\share\model.fold001.zip
        /// </summary>
        /// <param name="outputModelFile">Path to output model file</param>
        /// <param name="fold">Current fold</param>
        /// <returns>Path to output model file for specific fold</returns>
        public static string ConstructPerFoldName(string outputModelFile, int fold)
        {
            if (string.IsNullOrWhiteSpace(outputModelFile))
                return null;
            var fileName = Path.GetFileNameWithoutExtension(outputModelFile);

            return Path.Combine(Path.GetDirectoryName(outputModelFile),
             string.Format("{0}.fold{1:000}{2}", fileName, fold, Path.GetExtension(outputModelFile)));
        }
    }
}
