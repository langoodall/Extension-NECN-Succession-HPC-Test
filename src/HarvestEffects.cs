//  Copyright 2007-2010 Portland State University, University of Wisconsin-Madison
//  Author: Robert Scheller, Melissa Lucash

using Landis.Core;
using Landis.SpatialModeling;
using Edu.Wisc.Forest.Flel.Util;
using Landis.Library.LeafBiomassCohorts;

using System;
using System.Collections.Generic;


namespace Landis.Extension.Succession.NECN
{
    /// <summary>
    /// A helper class.
    /// </summary>
    public class HarvestReductions
    {
        private string prescription;
        private double coarseLitterReduction;
        private double fineLitterReduction;
        private double somReduction;
        private double cohortWoodReduction;
        private double cohortLeafReduction;

        public string PrescriptionName
        {
            get
            {
                return prescription;
            }
            set
            {
                if (value != null)
                    prescription = value;
            }
        }
        public double CoarseLitterReduction
        {
            get
            {
                return coarseLitterReduction;
            }
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new InputValueException(value.ToString(), "Coarse litter reduction due to fire must be between 0 and 1.0");
                coarseLitterReduction = value;
            }

        }
        public double FineLitterReduction
        {
            get
            {
                return fineLitterReduction;
            }
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new InputValueException(value.ToString(), "Fine litter reduction due to fire must be between 0 and 1.0");
                fineLitterReduction = value;
            }

        }
        public double CohortWoodReduction
        {
            get
            {
                return cohortWoodReduction;
            }
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new InputValueException(value.ToString(), "Cohort wood reduction due to fire must be between 0 and 1.0");
                cohortWoodReduction = value;
            }

        }
        public double CohortLeafReduction
        {
            get
            {
                return cohortLeafReduction;
            }
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new InputValueException(value.ToString(), "Cohort wood reduction due to fire must be between 0 and 1.0");
                cohortLeafReduction = value;
            }

        }
        public double SOMReduction
        {
            get
            {
                return somReduction;
            }
            set
            {
                if (value < 0.0 || value > 1.0)
                    throw new InputValueException(value.ToString(), "Soil Organic Matter (SOM) reduction due to fire must be between 0 and 1.0");
                somReduction = value;
            }

        }

        //---------------------------------------------------------------------
        public HarvestReductions()
        {
            this.prescription = "";
            this.CoarseLitterReduction = 0.0;
            this.FineLitterReduction = 0.0;
            this.CohortLeafReduction = 0.0;
            this.CohortWoodReduction = 0.0;
            this.SOMReduction = 0.0;
        }
    }

    public class HarvestEffects
    {

        public static double GetCohortWoodRemoval(ActiveSite site)
        {

            double woodRemoval = 1.0;  // Default is 100% removal
            if (SiteVars.HarvestPrescriptionName == null)
                return woodRemoval;

            foreach (HarvestReductions prescription in PlugIn.Parameters.HarvestReductionsTable)
            {
                //PlugIn.ModelCore.UI.WriteLine("   PrescriptionName={0}, Site={1}.", prescription.PrescriptionName, site);

                if (SiteVars.HarvestPrescriptionName[site].Trim() == prescription.PrescriptionName.Trim())
                {
                    woodRemoval = prescription.CohortWoodReduction;
                }
            }

            return woodRemoval;

        }

        public static double GetCohortLeafRemoval(ActiveSite site)
        {
            double leafRemoval = 0.0;  // Default is 0% removal
            if (SiteVars.HarvestPrescriptionName == null)
                return leafRemoval;

            foreach (HarvestReductions prescription in PlugIn.Parameters.HarvestReductionsTable)
            {
                if (SiteVars.HarvestPrescriptionName[site].Trim() == prescription.PrescriptionName.Trim())
                {
                    leafRemoval = prescription.CohortLeafReduction;
                }
            }

            return leafRemoval;

        }

        //---------------------------------------------------------------------
        /// <summary>
        /// Computes fire effects on litter, coarse woody debris, duff layer.
        /// </summary>
        public static void ReduceLayers(string prescriptionName, Site site)
        {
            PlugIn.ModelCore.UI.WriteLine("   Calculating harvest induced layer reductions...");

            double litterLossMultiplier = 0.0;
            double woodLossMultiplier = 0.0;
            double som_Multiplier = 0.0;

            bool found = false;
            foreach (HarvestReductions prescription in PlugIn.Parameters.HarvestReductionsTable)
            {
                if (SiteVars.HarvestPrescriptionName != null && prescriptionName.Trim() == prescription.PrescriptionName.Trim())
                {
                    litterLossMultiplier = prescription.FineLitterReduction;
                    woodLossMultiplier = prescription.CoarseLitterReduction;
                    som_Multiplier = prescription.SOMReduction;

                    found = true;
                }
            }
            if (!found)
            {
                PlugIn.ModelCore.UI.WriteLine("   Prescription {0} not found in the NECN Harvest Effects Table", prescriptionName);
                return;
            }
            //PlugIn.ModelCore.UI.WriteLine("   LitterLoss={0:0.00}, woodLoss={1:0.00}, SOM_loss={2:0.00}, SITE={3}", litterLossMultiplier, woodLossMultiplier, som_Multiplier, site);

            SiteVars.HarvestTime[site] = PlugIn.ModelCore.CurrentTime;

            // Structural litter

            double carbonLoss = SiteVars.SurfaceStructural[site].Carbon * litterLossMultiplier;
            double nitrogenLoss = SiteVars.SurfaceStructural[site].Nitrogen * litterLossMultiplier;

            SiteVars.SurfaceStructural[site].Carbon -= carbonLoss;
            SiteVars.SourceSink[site].Carbon        += carbonLoss;

            SiteVars.SurfaceStructural[site].Nitrogen -= nitrogenLoss;
            SiteVars.SourceSink[site].Nitrogen += nitrogenLoss;

            // Metabolic litter

            carbonLoss = SiteVars.SurfaceMetabolic[site].Carbon * litterLossMultiplier;
            nitrogenLoss = SiteVars.SurfaceMetabolic[site].Nitrogen * litterLossMultiplier;

            SiteVars.SurfaceMetabolic[site].Carbon  -= carbonLoss;
            SiteVars.SourceSink[site].Carbon        += carbonLoss;

            SiteVars.SurfaceMetabolic[site].Nitrogen -= nitrogenLoss;
            SiteVars.SourceSink[site].Nitrogen        += nitrogenLoss;

            // Surface dead wood
            carbonLoss   = SiteVars.SurfaceDeadWood[site].Carbon * woodLossMultiplier;
            nitrogenLoss = SiteVars.SurfaceDeadWood[site].Nitrogen * woodLossMultiplier;

            SiteVars.SurfaceDeadWood[site].Carbon   -= carbonLoss;
            SiteVars.SourceSink[site].Carbon        += carbonLoss;

            SiteVars.SurfaceDeadWood[site].Nitrogen -= nitrogenLoss;
            SiteVars.SourceSink[site].Nitrogen        += nitrogenLoss;

            // Soil Organic Matter (Duff)

            carbonLoss = SiteVars.SOM1surface[site].Carbon * som_Multiplier;
            nitrogenLoss = SiteVars.SOM1surface[site].Nitrogen * som_Multiplier;

            SiteVars.SOM1surface[site].Carbon -= carbonLoss;
            SiteVars.SourceSink[site].Carbon += carbonLoss;

            SiteVars.SOM1surface[site].Nitrogen -= nitrogenLoss;
            SiteVars.SourceSink[site].Nitrogen += nitrogenLoss;

        }

    }
}
