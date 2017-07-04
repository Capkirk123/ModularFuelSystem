//#define DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using KSP;

namespace RealFuels
{
    public class RefuelingPump : PartModule, IAnalyticPreview
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Fuel Pump")]
        [UI_Toggle(affectSymCounterparts = UI_Scene.Editor, disabledText = "Disabled", enabledText = "Enabled")]
        bool enablePump = true;

        [KSPField(isPersistant = true)]
        double pump_rate = 100.0; // 100L/sec per resource

        private FlightIntegrator flightIntegrator;

        public override string GetInfo ()
        {
            return "\nPump rate: " + pump_rate + "/s";
        }

		public override void OnStart (PartModule.StartState state)
		{
			if (HighLogic.LoadedSceneIsFlight)
            {
                FindFlightIntegrator();
                SetupGUI();
            }
		}

        public void FixedUpdate ()
        {
            if (HighLogic.LoadedSceneIsFlight && part.parent != null && part.vessel != null && !flightIntegrator.isAnalytical && enablePump)
                FillAttachedTanks(TimeWarp.fixedDeltaTime);
        }

        #region IAnalyticPreview

        public void AnalyticInfo(FlightIntegrator fi, double sunAndBodyIn, double backgroundRadiation, double radArea, double absEmissRatio, double internalFlux, double convCoeff, double ambientTemp, double maxPartTemp)
        {
            if (enablePump && fi.timeSinceLastUpdate < double.MaxValue * 0.99)
                FillAttachedTanks(fi.timeSinceLastUpdate);
        }

        public double InternalFluxAdjust() => 0;

        #endregion

        private void FindFlightIntegrator()
        {
            foreach (VesselModule module in vessel.vesselModules)
            {
                if (module is FlightIntegrator fi)
                {
                    flightIntegrator = fi;
                    break;
                }
            }

            if (flightIntegrator is null)
                Debug.LogError("[RefuelingPump] could not find flight integrator!");
        }

        private void SetupGUI()
        {
            BaseField field = Fields[nameof(enablePump)];
            if (vessel != null && vessel.LandedInKSC)
            {
                field.guiActive = true;
            }
            else
            {
                field.guiActive = false;
                enablePump = false;
            }
        }

        private void FillAttachedTanks(double deltaTime)
        {
            // sanity check
            if(deltaTime <= 0)
                return;

            // now, let's look at what we're connected to.
            foreach (Part p in vessel.parts ) // look through all parts
            {
                Tanks.ModuleFuelTanks m = p.FindModuleImplementing<Tanks.ModuleFuelTanks>();
                if (m != null)
                {
                    double minTemp = p.temperature;
                    m.fueledByLaunchClamp = true;
                    // look through all tanks inside this part
                    for (int j = m.tankList.Count - 1; j >= 0; --j)
                    {
                        Tanks.FuelTank tank = m.tankList[j];
                        // if a tank isn't full, start filling it.

                        if (tank.maxAmount <= 0) continue;

                        PartResource r = tank.resource;
                        if (r == null) continue;

                        PartResourceDefinition d = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                        if (d == null) continue;
                        
                        if (tank.loss_rate > 0d)
                            minTemp = Math.Min(minTemp, tank.temperature);
                        if (tank.amount < tank.maxAmount && tank.fillable && r.flowMode != PartResource.FlowMode.None && d.resourceTransferMode == ResourceTransferMode.PUMP && r.flowState)
                        {
                            double amount = Math.Min(deltaTime * pump_rate * tank.utilization, tank.maxAmount - tank.amount);
                            var game = HighLogic.CurrentGame;

                            if (d.unitCost > 0 && game.Mode == Game.Modes.CAREER && Funding.Instance != null)
                            {
                                double funds = Funding.Instance.Funds;
                                double cost = amount * d.unitCost;
                                if (cost > funds)
                                {
                                    amount = funds / d.unitCost;
                                    cost = funds;
                                }
                                Funding.Instance.AddFunds(-cost, TransactionReasons.VesselRollout);
                            }
                            //tank.amount = tank.amount + amount;
                            p.TransferResource(r, amount, this.part);
                        }
                    }
                    p.temperature = minTemp;
                }
                else
                {
                    for (int j = p.Resources.Count - 1; j >= 0; --j)
                    {
                        PartResource r = p.Resources[j];
                        if (r.info.name == "ElectricCharge")
                        {
                            if (r.flowMode != PartResource.FlowMode.None && r.info.resourceTransferMode == ResourceTransferMode.PUMP && r.flowState)
                            {
                                double amount = deltaTime * pump_rate;
                                amount = Math.Min(amount, r.maxAmount - r.amount);
                                p.TransferResource(r, amount, this.part);
                            }
                        }
                    }
                }
            }
        }
    }
}
