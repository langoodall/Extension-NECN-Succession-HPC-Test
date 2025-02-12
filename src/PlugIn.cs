//  Authors: Robert Scheller, Melissa Lucash

using Landis.Core;
using Landis.SpatialModeling;
using Landis.Utilities;

using Landis.Library.InitialCommunities;
using Landis.Library.Succession;
using Landis.Library.LeafBiomassCohorts;
using Landis.Library.Climate;
using Landis.Library.Metadata;

using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Distributions;



namespace Landis.Extension.Succession.NECN
{
    public class PlugIn
        : Landis.Library.Succession.ExtensionBase
    {
        public static readonly string ExtensionName = "NECN Succession";
        private static ICore modelCore;
        public static IInputParameters Parameters;
        public static double[] ShadeLAI;
        public static double AnnualWaterBalance;

        //private List<ISufficientLight> sufficientLight;
        public static string SoilCarbonMapNames = null;
        public static int SoilCarbonMapFrequency;
        public static string SoilNitrogenMapNames = null;
        public static int SoilNitrogenMapFrequency;
        public static string ANPPMapNames = null;
        public static int ANPPMapFrequency;
        public static string ANEEMapNames = null;
        public static int ANEEMapFrequency;
        public static string TotalCMapNames = null;
        public static int TotalCMapFrequency;
        public static string InputCommunityMapNames = null;
        public static int InputCommunityMapFrequency;
        public static int SuccessionTimeStep;
        public static double ProbEstablishAdjust;
        public static double StormFlowOverride = 0.0;


        public static int FutureClimateBaseYear;
        //public static int B_MAX;
        private ICommunity initialCommunity;

        public static int[] SpeciesByPlant;
        public static int[] SpeciesBySerotiny;
        public static int[] SpeciesByResprout;
        public static int[] SpeciesBySeed;

        //---------------------------------------------------------------------

        public PlugIn()
            : base(ExtensionName)
        {
        }

        //---------------------------------------------------------------------

        public override void LoadParameters(string dataFile,
                                            ICore mCore)
        {
            modelCore = mCore;
            InputParametersParser parser = new InputParametersParser();
            Parameters = Landis.Data.Load<IInputParameters>(dataFile, parser);

        }

        //---------------------------------------------------------------------

        public static ICore ModelCore
        {
            get
            {
                return modelCore;
            }
        }


        //---------------------------------------------------------------------

        public override void Initialize()
        {
            PlugIn.ModelCore.UI.WriteLine("Initializing {0} ...", ExtensionName);
            Timestep = Parameters.Timestep;
            SuccessionTimeStep = Timestep;
            //sufficientLight = Parameters.LightClassProbabilities;
            ProbEstablishAdjust = Parameters.ProbEstablishAdjustment;
            MetadataHandler.InitializeMetadata(Timestep, modelCore, SoilCarbonMapNames, SoilNitrogenMapNames, ANPPMapNames, ANEEMapNames, TotalCMapNames); 

            FunctionalType.Initialize(Parameters);
            SpeciesData.Initialize(Parameters);
            SiteVars.Initialize(); // chihiro; this method use functional type data for initializing decay value
            ReadMaps.ReadSoilDepthMap(Parameters.SoilDepthMapName);
            ReadMaps.ReadSoilDrainMap(Parameters.SoilDrainMapName);
            ReadMaps.ReadSoilBaseFlowMap(Parameters.SoilBaseFlowMapName);
            ReadMaps.ReadSoilStormFlowMap(Parameters.SoilStormFlowMapName);
            ReadMaps.ReadFieldCapacityMap(Parameters.SoilFieldCapacityMapName);
            ReadMaps.ReadWiltingPointMap(Parameters.SoilWiltingPointMapName);
            ReadMaps.ReadPercentSandMap(Parameters.SoilPercentSandMapName);
            ReadMaps.ReadPercentClayMap(Parameters.SoilPercentClayMapName);
            ReadMaps.ReadSoilCNMaps(Parameters.InitialSOM1CSurfaceMapName,
                Parameters.InitialSOM1NSurfaceMapName,
                Parameters.InitialSOM1CSoilMapName,
                Parameters.InitialSOM1NSoilMapName,
                Parameters.InitialSOM2CMapName,
                Parameters.InitialSOM2NMapName,
                Parameters.InitialSOM3CMapName,
                Parameters.InitialSOM3NMapName);
            ReadMaps.ReadDeadWoodMaps(Parameters.InitialDeadSurfaceMapName, Parameters.InitialDeadSoilMapName);

            //Optional drought mortality maps
            if (Parameters.NormalSWAMapName != null)
            {
                ReadMaps.ReadNormalSWAMap(Parameters.NormalSWAMapName);
            }
            if (Parameters.NormalCWDMapName != null)
            {
                ReadMaps.ReadNormalCWDMap(Parameters.NormalCWDMapName);
            }
            if (Parameters.NormalTempMapName != null)
            {
                ReadMaps.ReadNormalTempMap(Parameters.NormalTempMapName);
            }

            //Optional topographic maps for adjusting PET
            if (Parameters.SlopeMapName != null)
            {
                ReadMaps.ReadSlopeMap(Parameters.SlopeMapName);
            }
            if (Parameters.AspectMapName != null)
            {
                ReadMaps.ReadAspectMap(Parameters.AspectMapName);
            }

            //Initialize climate.
            Climate.Initialize(Parameters.ClimateConfigFile, false, modelCore);
            FutureClimateBaseYear = Climate.Future_MonthlyData.Keys.Min();
            ClimateRegionData.Initialize(Parameters);

            //ShadeLAI = Parameters.MaximumShadeLAI;
            OtherData.Initialize(Parameters);
            FireEffects.Initialize(Parameters);

            //  Cohorts must be created before the base class is initialized
            //  because the base class' reproduction module uses the core's
            //  SuccessionCohorts property in its Initialization method.
            Library.LeafBiomassCohorts.Cohorts.Initialize(Timestep, new CohortBiomass());

            // Initialize Reproduction routines:
            Reproduction.SufficientResources = SufficientLight;
            Reproduction.Establish = Establish;
            Reproduction.AddNewCohort = AddNewCohort;
            Reproduction.MaturePresent = MaturePresent;
            base.Initialize(modelCore, Parameters.SeedAlgorithm);

            // Delegate mortality routines:
            Landis.Library.LeafBiomassCohorts.Cohort.PartialDeathEvent += CohortPartialMortality;
            Landis.Library.LeafBiomassCohorts.Cohort.DeathEvent += CohortTotalMortality;

            InitializeSites(Parameters.InitialCommunities, Parameters.InitialCommunitiesMap, modelCore);

            if (DroughtMortality.UseDrought | DroughtMortality.OutputSoilWaterAvailable | DroughtMortality.OutputTemperature | DroughtMortality.OutputClimateWaterDeficit)
            {
                DroughtMortality.Initialize(Parameters);
            }

            foreach (ActiveSite site in PlugIn.ModelCore.Landscape)
            {
                Main.ComputeTotalCohortCN(site, SiteVars.Cohorts[site]);
                SiteVars.FineFuels[site] = (SiteVars.SurfaceStructural[site].Carbon + SiteVars.SurfaceMetabolic[site].Carbon) * 2.0;
            }

            Outputs.WritePrimaryLogFile(0);
            Outputs.WriteShortPrimaryLogFile(0);


        }

        //---------------------------------------------------------------------

        public override void Run()
        {

            if (PlugIn.ModelCore.CurrentTime > 0)
            {
                Disturbed.ActiveSiteValues = false;
                SiteVars.ResetDisturbances();
            }

            ClimateRegionData.AnnualNDeposition = new Landis.Library.Parameters.Ecoregions.AuxParm<double>(PlugIn.ModelCore.Ecoregions);
            SpeciesByPlant = new int[ModelCore.Species.Count];
            SpeciesByResprout = new int[ModelCore.Species.Count];
            SpeciesBySerotiny = new int[ModelCore.Species.Count];
            SpeciesBySeed = new int[ModelCore.Species.Count];

            //base.RunReproductionFirst();

            base.Run();

            if (Timestep > 0)
                ClimateRegionData.SetAllEcoregions_FutureAnnualClimate(ModelCore.CurrentTime);

            if (ModelCore.CurrentTime % Timestep == 0)
            {
                // Write monthly log file:
                // Output must reflect the order of operation:
                int[] months = new int[12] { 6, 7, 8, 9, 10, 11, 0, 1, 2, 3, 4, 5 };

                if (OtherData.CalibrateMode)
                    months = new int[12] { 6, 7, 8, 9, 10, 11, 0, 1, 2, 3, 4, 5 };

                for (int i = 0; i < 12; i++)
                {
                    int month = months[i];
                    Outputs.WriteMonthlyLogFile(month);
                }
                Outputs.WritePrimaryLogFile(PlugIn.ModelCore.CurrentTime);
                Outputs.WriteShortPrimaryLogFile(PlugIn.ModelCore.CurrentTime);
                Outputs.WriteMaps();
                Outputs.WriteReproductionLog(PlugIn.ModelCore.CurrentTime);
                Establishment.LogEstablishment();
                if (PlugIn.InputCommunityMapNames != null && ModelCore.CurrentTime % PlugIn.InputCommunityMapFrequency == 0)
                    Outputs.WriteCommunityMaps();
                if(DroughtMortality.UseDrought)   Outputs.WriteDroughtSpeciesFile(PlugIn.ModelCore.CurrentTime);
            }

        }


        //---------------------------------------------------------------------
        // Although this function is no longer referenced, it is required through inheritance from the succession library

        public override byte ComputeShade(ActiveSite site)
        {

            return (byte) SiteVars.LAI[site]; // finalShade;
        }


        //---------------------------------------------------------------------

        protected override void InitializeSite(ActiveSite site)
        {

            InitialBiomass initialBiomass = InitialBiomass.Compute(site, initialCommunity);
            SiteVars.MineralN[site] = Parameters.InitialMineralN;
        }


        //---------------------------------------------------------------------
        // This method does not trigger reproduction
        public void CohortPartialMortality(object sender, Landis.Library.BiomassCohorts.PartialDeathEventArgs eventArgs)
        {
            if(OtherData.CalibrateMode) PlugIn.ModelCore.UI.WriteLine("Cohort Partial Mortality:  {0}", eventArgs.Site);

            ExtensionType disturbanceType = eventArgs.DisturbanceType;
            ActiveSite site = eventArgs.Site;

            ICohort cohort = (Landis.Library.LeafBiomassCohorts.ICohort)eventArgs.Cohort;

            float fractionPartialMortality = (float)eventArgs.Reduction;
            float foliarInput = cohort.LeafBiomass * fractionPartialMortality;
            float woodInput = cohort.WoodBiomass * fractionPartialMortality;

            if (disturbanceType.IsMemberOf("disturbance:harvest"))
            {
                SiteVars.HarvestPrescriptionName = PlugIn.ModelCore.GetSiteVar<string>("Harvest.PrescriptionName");
                if (ModelCore.CurrentTime > SiteVars.HarvestDisturbedYear[site]) // this is the first cohort killed/damaged
                {
                    //PlugIn.ModelCore.UI.WriteLine("   Begin harvest layer reductions...");
                    HarvestEffects.ReduceLayers(SiteVars.HarvestPrescriptionName[site], site);
                    SiteVars.HarvestDisturbedYear[site] = ModelCore.CurrentTime;
                }
                woodInput -= woodInput * (float)HarvestEffects.GetCohortWoodRemoval(site);
                foliarInput -= foliarInput * (float)HarvestEffects.GetCohortLeafRemoval(site);
            }
            if (disturbanceType.IsMemberOf("disturbance:fire"))
            {

                SiteVars.FireSeverity = PlugIn.ModelCore.GetSiteVar<byte>("Fire.Severity");

                if (ModelCore.CurrentTime > SiteVars.FireDisturbedYear[site]) // this is the first cohort killed/damaged
                {
                    SiteVars.SmolderConsumption[site] = 0.0;
                    SiteVars.FlamingConsumption[site] = 0.0;
                    if (SiteVars.FireSeverity != null && SiteVars.FireSeverity[site] > 0)
                        FireEffects.ReduceLayers(SiteVars.FireSeverity[site], site);

                    SiteVars.FireDisturbedYear[site] = ModelCore.CurrentTime;

                }

                double live_woodFireConsumption = woodInput * (float)FireEffects.ReductionsTable[(int)SiteVars.FireSeverity[site]].CohortWoodReduction;
                double live_foliarFireConsumption = foliarInput * (float)FireEffects.ReductionsTable[(int)SiteVars.FireSeverity[site]].CohortLeafReduction;

                SiteVars.SmolderConsumption[site] += live_woodFireConsumption;
                SiteVars.FlamingConsumption[site] += live_foliarFireConsumption;
                SiteVars.SourceSink[site].Carbon += live_woodFireConsumption * 0.47;
                SiteVars.SourceSink[site].Carbon += live_foliarFireConsumption * 0.47;
                woodInput -= (float)live_woodFireConsumption;
                foliarInput -= (float)live_foliarFireConsumption;

            }
            if (disturbanceType.IsMemberOf("disturbance:browse"))
            {
                //SF Initial effort to account for browsed biomass nutrient cycling
                //all browser waste treated as leaves with high N content. This overestimates moose waste if 
                //there is a lot of cohort mortality versus browse eaten. 

                foliarInput += woodInput; 
                woodInput = 0;

                double inputDecayValue = 1.0;   // Decay value is calculated for surface/soil layers (leaf/fine root), 
                                                // therefore, this is just a dummy value.

                if (foliarInput > 0)
                {
                    SiteVars.LitterfallC[site] += foliarInput * 0.47;
                    foliarInput = foliarInput * (float) 0.1;                     //most carbon is respired


                    //PlugIn.ModelCore.UI.WriteLine("waste input is {0}, CN ratio of waste is {1}", foliarInput, 50);

                    //Nitrogen content of feces is approximately 1.6% for deer(Asada and Ochiai 1999),
                    //between 1.45% and 2.26% for deer (Howery and Pfister, 1990),
                    //2.5%  for deer (Euan et al. 2020),
                    //1.33% in winter, 2.44% for moose in summer (Persson et al. 2000),
                    //2.4% for moose (Kuijper et al. 2016)
                    //Feces N = 5.7 kg per moose per year (Persson et al. 2000)

                    //Amount of nitrogen in urine is 0.5% in summer (Persson et al. 2000)
                    //3675 L urine per moose per year (Persson et al. 2000)
                    //Urine is 0.5% N = 18.375 kg N per year per moose (assuming summer and winter N content is the same)

                    //Total N for moose waste = 24 kg per moose per year
                    //Each moose eats 2738 kg biomass per year
                    //Foliar inputs are 2738 * 0.47 * 0.1 kg C  = 128.67 kg C per moose
                    //CN ratio = 128/24 = 5.33

                    LitterLayer.PartitionResidue(
                                foliarInput,
                                inputDecayValue,
                                5.33, //CN ratio for browse waste -- metabolic
                                1, //"lignin" content of waste
                                5.33, //CN ratio for browse waste -- structural
                                LayerName.Leaf,
                                LayerType.Surface,
                                site);
                    //PlugIn.ModelCore.UI.WriteLine("EVENT: Cohort Partial Mortality: species={0}, age={1}, disturbance={2}.", cohort.Species.Name, cohort.Age, disturbanceType);
                    //PlugIn.ModelCore.UI.WriteLine("       Cohort Reductions:  Foliar={0:0.00}.  Wood={1:0.00}.", HarvestEffects.GetCohortLeafRemoval(site), HarvestEffects.GetCohortLeafRemoval(site));
                    //PlugIn.ModelCore.UI.WriteLine("       InputB/TotalB:  Foliar={0:0.00}/{1:0.00}, Wood={2:0.0}/{3:0.0}.", foliarInput, cohort.LeafBiomass, woodInput, cohort.WoodBiomass);

                    Disturbed[site] = false; 

                    return;
                }
            }
            
            if (SpeciesData.Grass[cohort.Species])
            {
                ForestFloor.AddFoliageLitter(woodInput + foliarInput, cohort.Species, site);  //  Wood biomass of grass species is transfered to non wood litter. (W.Hotta 2021.12.16)

                Roots.AddFineRootLitter(Roots.CalculateFineRoot(cohort, (cohort.WoodBiomass + cohort.LeafBiomass) * fractionPartialMortality), cohort, cohort.Species, site);
            }
            else
            {
                ForestFloor.AddWoodLitter(woodInput, cohort.Species, site);
                ForestFloor.AddFoliageLitter(foliarInput, cohort.Species, site);

                Roots.AddCoarseRootLitter(Roots.CalculateCoarseRoot(cohort, cohort.WoodBiomass * fractionPartialMortality), cohort, cohort.Species, site);
                Roots.AddFineRootLitter(Roots.CalculateFineRoot(cohort, cohort.LeafBiomass * fractionPartialMortality), cohort, cohort.Species, site);
                
            }
            
            //PlugIn.ModelCore.UI.WriteLine("EVENT: Cohort Partial Mortality: species={0}, age={1}, disturbance={2}.", cohort.Species.Name, cohort.Age, disturbanceType);
            //PlugIn.ModelCore.UI.WriteLine("       Cohort Reductions:  Foliar={0:0.00}.  Wood={1:0.00}.", HarvestEffects.GetCohortLeafRemoval(site), HarvestEffects.GetCohortLeafRemoval(site));
            //PlugIn.ModelCore.UI.WriteLine("       InputB/TotalB:  Foliar={0:0.00}/{1:0.00}, Wood={2:0.0}/{3:0.0}.", foliarInput, cohort.LeafBiomass, woodInput, cohort.WoodBiomass);
            Disturbed[site] = true;

            return;
        }
        //---------------------------------------------------------------------
        // Total mortality, including from disturbance or senescence.

        public void CohortTotalMortality(object sender, Landis.Library.BiomassCohorts.DeathEventArgs eventArgs)
        {

            //PlugIn.ModelCore.UI.WriteLine("Cohort Total Mortality: {0}", eventArgs.Site);

            ExtensionType disturbanceType = eventArgs.DisturbanceType;

            ActiveSite site = eventArgs.Site;

            ICohort cohort = (Landis.Library.LeafBiomassCohorts.ICohort)eventArgs.Cohort;
            double foliarInput = (double)cohort.LeafBiomass;
            double woodInput = (double)cohort.WoodBiomass;

            if (disturbanceType != null)
            {
                //PlugIn.ModelCore.UI.WriteLine("DISTURBANCE EVENT: Cohort Died: species={0}, age={1}, disturbance={2}.", cohort.Species.Name, cohort.Age, eventArgs.DisturbanceType);

                if (disturbanceType.IsMemberOf("disturbance:fire"))
                {
                    SiteVars.FireSeverity = PlugIn.ModelCore.GetSiteVar<byte>("Fire.Severity");
                    Landis.Library.Succession.Reproduction.CheckForPostFireRegen(eventArgs.Cohort, site);

                    if (ModelCore.CurrentTime > SiteVars.FireDisturbedYear[site])  // the first cohort killed/damaged
                    {
                        SiteVars.SmolderConsumption[site] = 0.0;
                        SiteVars.FlamingConsumption[site] = 0.0;
                        if (SiteVars.FireSeverity != null && SiteVars.FireSeverity[site] > 0)
                            FireEffects.ReduceLayers(SiteVars.FireSeverity[site], site);

                        SiteVars.FireDisturbedYear[site] = ModelCore.CurrentTime;

                    }

                    double woodFireConsumption = woodInput * (float)FireEffects.ReductionsTable[(int)SiteVars.FireSeverity[site]].CohortWoodReduction;
                    double foliarFireConsumption = foliarInput * (float)FireEffects.ReductionsTable[(int)SiteVars.FireSeverity[site]].CohortLeafReduction;

                    SiteVars.SmolderConsumption[site] += woodFireConsumption;
                    SiteVars.FlamingConsumption[site] += foliarFireConsumption;
                    SiteVars.SourceSink[site].Carbon += woodFireConsumption * 0.47;
                    SiteVars.SourceSink[site].Carbon += foliarFireConsumption * 0.47;
                    woodInput -= woodFireConsumption;
                    foliarInput -= foliarFireConsumption;

                }
                else
                {
                    if (disturbanceType.IsMemberOf("disturbance:harvest"))
                    {
                        SiteVars.HarvestPrescriptionName = PlugIn.ModelCore.GetSiteVar<string>("Harvest.PrescriptionName");
                        if (ModelCore.CurrentTime > SiteVars.HarvestDisturbedYear[site])  // the first cohort killed/damaged
                        {
                            HarvestEffects.ReduceLayers(SiteVars.HarvestPrescriptionName[site], site);
                        }
                        double woodLoss = woodInput * (float)HarvestEffects.GetCohortWoodRemoval(site);
                        double foliarLoss = foliarInput * (float)HarvestEffects.GetCohortLeafRemoval(site);
                        SiteVars.SourceSink[site].Carbon += woodLoss * 0.47;
                        SiteVars.SourceSink[site].Carbon += foliarLoss * 0.47;
                        woodInput -= woodLoss;
                        foliarInput -= foliarLoss;
                        SiteVars.HarvestDisturbedYear[site] = ModelCore.CurrentTime;
                    }

                    // If not fire, check for resprouting:
                    Landis.Library.Succession.Reproduction.CheckForResprouting(eventArgs.Cohort, site);
                }
            }


            if (SpeciesData.Grass[cohort.Species])
            {
                //PlugIn.ModelCore.UI.WriteLine("Cohort Died: species={0}, age={1}, wood={2:0.00}, foliage={3:0.00}.", cohort.Species.Name, cohort.Age, wood, foliar);
                ForestFloor.AddFoliageLitter(woodInput + foliarInput, cohort.Species, eventArgs.Site);  //  Wood biomass of grass species is transfered to non wood litter. (W.Hotta 2021.12.16)

                // Assume that ALL dead root biomass stays on site.
                Roots.AddFineRootLitter(Roots.CalculateFineRoot(cohort, cohort.WoodBiomass + cohort.LeafBiomass), cohort, cohort.Species, eventArgs.Site);
            }
            else
            {
                //PlugIn.ModelCore.UI.WriteLine("Cohort Died: species={0}, age={1}, wood={2:0.00}, foliage={3:0.00}.", cohort.Species.Name, cohort.Age, wood, foliar);
                ForestFloor.AddWoodLitter(woodInput, cohort.Species, eventArgs.Site);
                ForestFloor.AddFoliageLitter(foliarInput, cohort.Species, eventArgs.Site);

                // Assume that ALL dead root biomass stays on site.
                Roots.AddCoarseRootLitter(Roots.CalculateCoarseRoot(cohort, cohort.WoodBiomass), cohort, cohort.Species, eventArgs.Site);
                Roots.AddFineRootLitter(Roots.CalculateFineRoot(cohort, cohort.LeafBiomass), cohort, cohort.Species, eventArgs.Site);
            }
            

            if (disturbanceType != null)
                Disturbed[site] = true;

            return;
        }

        //---------------------------------------------------------------------
        //Grows the cohorts for future climate
        protected override void AgeCohorts(ActiveSite site,
                                           ushort years,
                                           int? successionTimestep)
        {
            Main.Run(site, years, successionTimestep.HasValue);

        }
        //---------------------------------------------------------------------
        /// <summary>
        /// Determines if there is sufficient light at a site for a species to
        /// germinate/resprout.
        /// This is a Delegate method to base succession.
        /// </summary>
        /// 
        // W.Hotta and Chihiro modified
        // 
        // Description:
        //     - Modify light probability based on the amount of nursery log on the site
        //
        //
        
        public bool SufficientLight(ISpecies species, ActiveSite site)
        {

            //PlugIn.ModelCore.UI.WriteLine("  Calculating Sufficient Light from Succession.");
            //byte siteShade = PlugIn.ModelCore.GetSiteVar<byte>("Shade")[site];
            //int bestShadeClass = 0; // the best shade class for the species; Chihiro
            //bool found = false;
            bool isSufficientlight = false;
            double lightProbability = 0.0;

            string regenType = "failed"; // Identify where the cohort established; Chihiro
            //SF regenType is only used in CalibrateMode

            var random = new Troschuetz.Random.TRandom();
            double a = SpeciesData.LightLAIShape[species];
            double b = SpeciesData.LightLAIScale[species];
            double c = SpeciesData.LightLAILocation[species];
            double adjust = SpeciesData.LightLAIAdjust[species];
            double lai = SiteVars.LAI[site];

            lightProbability = adjust * (((a / b) * Math.Pow((lai / b), (a - 1)) * Math.Exp(-Math.Pow((lai / b), a))) + c); //3-parameter Weibull PDF equation
            lightProbability = Math.Min(lightProbability, 1.0);
            //if(OtherData.CalibrateMode) PlugIn.ModelCore.UI.WriteLine("Estimated Weibull light probability for species {0} = {1:0.000}, at LAI = {2:0.00}", species.Name, lightProbability, SiteVars.LAI[site]);
            
            //double randomLAI = PlugIn.ModelCore.NormalDistribution.NextDouble();
            if (modelCore.GenerateUniform() < lightProbability)
                isSufficientlight = true;

            // ------------------------------------------------------------------------
            // Modify light probability based on the amount of nursery log on the site
            // W.Hotta 2020.01.22
            //
            // Compute the availability of nursery log on the site
            //   Option1: function type is linear
            //   Option2: function type is power

            if (!SpeciesData.NurseLog_depend[species])
                return isSufficientlight;

            double nurseryLogAvailabilityModifier = 2.0; // tuning parameter (only even)
            double nurseryLogAvailability = 1 - Math.Pow(ComputeNurseryLogAreaRatio(species, site) - 1, nurseryLogAvailabilityModifier);
            if (OtherData.CalibrateMode)
            {
                PlugIn.ModelCore.UI.WriteLine("original_lightProbability:{0},{1},{2}", PlugIn.ModelCore.CurrentTime, species.Name, lightProbability);
                //PlugIn.ModelCore.UI.WriteLine("siteShade:{0}", siteShade);
                PlugIn.ModelCore.UI.WriteLine("siteLAI:{0}", SiteVars.LAI[site]);
            }

            // Case 1. CWD-dependent species (species which can only be established on nursery log)
            if (SpeciesData.NurseLog_depend[species]) // W.Hotta (2021.08.01)
            {
                lightProbability *= nurseryLogAvailability;
                isSufficientlight = modelCore.GenerateUniform() < lightProbability;
                if (isSufficientlight) regenType = "nurse_log";
            }
            // Case 2. CWD-independent species (species which can be established on both forest floor & nursery log)
            else
            {
                // 1. Can the cohort establish on forest floor? (lightProbability is considering both Tree and Grass species)
                if (modelCore.GenerateUniform() < lightProbability)
                {
                    isSufficientlight = true;
                    regenType = "surface";
                }
                //else
                //{
                //    // 2. If (1) the site shade is darker than the best shade class for the species and 
                //    //       (2) the light availability meets the species requirement,
                //    //if (siteShade > bestShadeClass && modelCore.GenerateUniform() < lightProbability)
                //    //{
                //        // 3. check if threre are sufficient amounts of downed logs?
                //        isSufficientlight = modelCore.GenerateUniform() < nurseryLogAvailability;
                //        if (isSufficientlight) regenType = "nlog";
                //    //}
                //}
                if (OtherData.CalibrateMode)
                {
                    PlugIn.ModelCore.UI.WriteLine("nurseryLogPenalty:{0},{1},{2}", PlugIn.ModelCore.CurrentTime, species.Name, nurseryLogAvailability);
                    PlugIn.ModelCore.UI.WriteLine("modified_lightProbability:{0},{1},{2}", PlugIn.ModelCore.CurrentTime, species.Name, lightProbability);
                    PlugIn.ModelCore.UI.WriteLine("regeneration_type:{0},{1},{2}", PlugIn.ModelCore.CurrentTime, species.Name, regenType);
                }
            }

            return isSufficientlight;
        }

        //---------------------------------------------------------------------
        /// <summary>
        /// Compute the most suitable shade class for the species
        /// This function identifies the peak of the light establishment table.
        /// </summary>
        // Chihiro 2020.01.22
        //
        //private static int ComputeBestShadeClass(ISufficientLight lights)
        //{
        //    int bestShadeClass = 0;
        //    double maxProbabilityLight = 0.0;
        //    if (lights.ProbabilityLight0 > maxProbabilityLight) bestShadeClass = 0;
        //    if (lights.ProbabilityLight1 > maxProbabilityLight) bestShadeClass = 1;
        //    if (lights.ProbabilityLight2 > maxProbabilityLight) bestShadeClass = 2;
        //    if (lights.ProbabilityLight3 > maxProbabilityLight) bestShadeClass = 3;
        //    if (lights.ProbabilityLight4 > maxProbabilityLight) bestShadeClass = 4;
        //    if (lights.ProbabilityLight5 > maxProbabilityLight) bestShadeClass = 5;
        //    return bestShadeClass;

        //}


        //---------------------------------------------------------------------
        /// <summary>
        /// Compute the ratio of projected area (= occupancy area) of nursery logs to the grid area.
        /// </summary>
        // W.Hotta & Chihiro;
        //
        // Description: 
        //     - Every SiteVars.CurrentDeadWoodC[site] is downed logs.
        //     - Only the downed logs (SiteVars.CurrentDeadWoodC[site]) which decay class is between 3 to 5 
        //       are suitable for establishment and treated as nursery logs.
        //     - The carbon stocks of the nursery logs are converted to volume 
        //       using a wood density of each decay class.
        //     - Then, the volume is converted to the projected area (occupation area) 
        //       using the mean height of downed logs derived from field data.
        //         - The shape of downed logs were assumed to be an elliptical cylinder
        //
        //
        
        private static double ComputeNurseryLogAreaRatio(ISpecies species, ActiveSite site)
        {
            // Hight of downed logs
            double hight = 28.64; // Units: cm

            // Wood density (g cm^-3) of dead wood for each decay class.
            // Decay class 3-5 is suitable for establishment.
            // Reference: Unidentified spp category in Table 3 of Ugawa et al. (2012)
            //            https://www.ffpri.affrc.go.jp/pubs/bulletin/425/documents/425-2.pdf
            double densityDecayClass0 = 0.421;
            double densityDecayClass3 = 0.255;
            double densityDecayClass4 = 0.178;
            double densityDecayClass5 = 0.112;

            // Compute the amount of nursery log carbon (gC m^-2)
            double[] nurseryLogC = ComputeNurseryLogC(site, densityDecayClass0, densityDecayClass3, densityDecayClass4, densityDecayClass5);

            // Compute the area ratio in the site of the nursery log occupies.
            // The shape of downed logs were assumed to be an elliptical cylinder
            // Variables:
            //   decayClassXAreaRatio (-)
            //   nurseryLogC[X] (gC m^-2)
            //   height (cm)
            //   densityDecayClass[X] (gC cm^-3)
            double decayClass3AreaRatio = 4 * 2 * nurseryLogC[0] / (Math.PI * hight * densityDecayClass3) * Math.Pow(10, -4); // Decay class 3
            double decayClass4AreaRatio = 4 * 2 * nurseryLogC[1] / (Math.PI * hight * densityDecayClass4) * Math.Pow(10, -4); // Decay class 4
            double decayClass5AreaRatio = 4 * 2 * nurseryLogC[2] / (Math.PI * hight * densityDecayClass5) * Math.Pow(10, -4); // Decay class 5
            if (OtherData.CalibrateMode && species.Index == 0)
            {
                PlugIn.ModelCore.UI.WriteLine("nurseryLogC:{0},{1},{2},{3}", PlugIn.ModelCore.CurrentTime, nurseryLogC[0], nurseryLogC[1], nurseryLogC[2]);
                PlugIn.ModelCore.UI.WriteLine("decayClassAreaRatios:{0},{1},{2},{3}", PlugIn.ModelCore.CurrentTime, decayClass3AreaRatio, decayClass4AreaRatio, decayClass5AreaRatio);
            }
            return Math.Min(1.0, decayClass3AreaRatio + decayClass4AreaRatio + decayClass5AreaRatio);
        }


        //---------------------------------------------------------------------
        /// <summary>
        /// Compute the amount of nursery log carbon based on its decay ratio
        /// </summary>
        // W.Hotta & Chihiro; 
        //
        // Description: 
        //     - In the process of decomposition of downed logs, 
        //       the volume remains the same, only the density changes.
        //
        
        private static double[] ComputeNurseryLogC(ActiveSite site, double densityDecayClass0, double densityDecayClass3, double densityDecayClass4, double densityDecayClass5)
        {
            // Define thresholds to identify decay class
            double retentionRatioThreshold3 = densityDecayClass3 / densityDecayClass0;
            double retentionRatioThreshold4 = densityDecayClass4 / densityDecayClass0;
            double retentionRatioThreshold5 = densityDecayClass5 / densityDecayClass0;

            // Initialize nursery log carbon for each decay class
            double decayClass3 = 0.0;
            double decayClass4 = 0.0;
            double decayClass5 = 0.0;

            // Update the amount of carbon for each decayClass
            for (int i = 0; i < SiteVars.CurrentDeadWoodC[site].Length; i++)
            {
                // Compute the ratio of the current dead wood C to the origindal dead wood C
                double retentionRatio = SiteVars.CurrentDeadWoodC[site][i] / SiteVars.OriginalDeadWoodC[site][i];
                // PlugIn.ModelCore.UI.WriteLine("decayRatio:{0},{1}", PlugIn.ModelCore.CurrentTime, decayRatio);

                // Identify the decay class of the current dead wood carbon & update the amount of C of each decay class (i.e. the amount of carbon just after the focused dead wood was generated.)
                if (retentionRatio >= retentionRatioThreshold4 & retentionRatio < retentionRatioThreshold3)
                {
                    decayClass3 += SiteVars.CurrentDeadWoodC[site][i];
                }
                else if (retentionRatio >= retentionRatioThreshold5 & retentionRatio < retentionRatioThreshold4)
                {
                    decayClass4 += SiteVars.CurrentDeadWoodC[site][i];
                }
                else if (retentionRatio < retentionRatioThreshold5)
                {
                    decayClass5 += SiteVars.CurrentDeadWoodC[site][i];
                }
            }
            // PlugIn.ModelCore.UI.WriteLine("decayClasses:{0},{1},{2},{3}", PlugIn.ModelCore.CurrentTime, decayClass3, decayClass4, decayClass5);
            return new double[3] { decayClass3, decayClass4, decayClass5 };
        }


        //---------------------------------------------------------------------
        /// <summary>
        /// Add a new cohort to a site following reproduction or planting.  Does not include initial communities.
        /// This is a Delegate method to base succession.
        /// </summary>

        public void AddNewCohort(ISpecies species, ActiveSite site, string reproductionType)
        {
            float[] initialBiomass = CohortBiomass.InitialBiomass(species, SiteVars.Cohorts[site], site);
            SiteVars.Cohorts[site].AddNewCohort(species, 1, initialBiomass[0], initialBiomass[1]);

            if (reproductionType == "plant")
                SpeciesByPlant[species.Index]++;
            else if (reproductionType == "serotiny")
                SpeciesBySerotiny[species.Index]++;
            else if (reproductionType == "resprout")
                SpeciesByResprout[species.Index]++;
            else if (reproductionType == "seed")
                SpeciesBySeed[species.Index]++;

        }
        //---------------------------------------------------------------------
        /// <summary>
        /// Determines if a species can establish on a site.
        /// This is a Delegate method to base succession.
        /// </summary>
        public bool Establish(ISpecies species, ActiveSite site)
        {
            double establishProbability = Establishment.Calculate(species, site);

            return modelCore.GenerateUniform() < establishProbability;
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Determines if a species can establish on a site.
        /// This is a Delegate method to base succession.
        /// </summary>
        public bool PlantingEstablish(ISpecies species, ActiveSite site)
        {
            IEcoregion ecoregion = modelCore.Ecoregion[site];
            double establishProbability = Establishment.Calculate(species, site);

            return establishProbability > 0.0;
        }

        //---------------------------------------------------------------------

        /// <summary>
        /// Determines if there is a mature cohort at a site.
        /// This is a Delegate method to base succession.
        /// </summary>
        public bool MaturePresent(ISpecies species, ActiveSite site)
        {
            return SiteVars.Cohorts[site].IsMaturePresent(species);
        }


        public override void InitializeSites(string initialCommunitiesText, string initialCommunitiesMap, ICore modelCore)
        {
            ModelCore.UI.WriteLine("   Loading initial communities from file \"{0}\" ...", initialCommunitiesText);
            Landis.Library.InitialCommunities.DatasetParser parser = new Landis.Library.InitialCommunities.DatasetParser(Timestep, ModelCore.Species);
            Landis.Library.InitialCommunities.IDataset communities = Landis.Data.Load<Landis.Library.InitialCommunities.IDataset>(initialCommunitiesText, parser);

            ModelCore.UI.WriteLine("   Reading initial communities map \"{0}\" ...", initialCommunitiesMap);
            IInputRaster<uintPixel> map;
            map = ModelCore.OpenRaster<uintPixel>(initialCommunitiesMap);
            using (map)
            {
                uintPixel pixel = map.BufferPixel;
                foreach (Site site in ModelCore.Landscape.AllSites)
                {
                    map.ReadBufferPixel();
                    uint mapCode = pixel.MapCode.Value;
                    if (!site.IsActive)
                        continue;

                    //if (!modelCore.Ecoregion[site].Active)
                    //    continue;

                    //modelCore.Log.WriteLine("ecoregion = {0}.", modelCore.Ecoregion[site]);

                    ActiveSite activeSite = (ActiveSite)site;
                    initialCommunity = communities.Find(mapCode);
                    if (initialCommunity == null)
                    {
                        throw new ApplicationException(string.Format("Unknown map code for initial community: {0}", mapCode));
                    }

                    InitializeSite(activeSite); 
                }
            }
        }
    }
    }