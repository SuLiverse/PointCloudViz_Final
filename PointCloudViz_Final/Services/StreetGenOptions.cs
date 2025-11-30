using System;

namespace PointCloudViz_Final.Services
{
    public class StreetGenOptions
    {
        public double RoadLength { get; set; } = 80.0;
        public double RoadWidth  { get; set; } = 10.0;
        public double Step       { get; set; } = 0.08;

        public double SidewalkWidth { get; set; } = 1.6;
        public double CurbHeight    { get; set; } = 0.14;
        public double BuildingHeight{ get; set; } = 9.0;

        public bool EnableBuildings { get; set; } = true;
        public bool EnableCars      { get; set; } = true;
        public bool EnableTrees     { get; set; } = true;
        public bool EnablePoles     { get; set; } = true;

        public int CarCount   { get; set; } = 6;
        public int TreeCount  { get; set; } = 8;
        public int PoleCount  { get; set; } = 6;

        public int Seed { get; set; } = 2025;

        public double CenterDashPeriod { get; set; } = 6.0;
        public double CenterDashOn     { get; set; } = 2.4;
        public double CenterHalfWidth  { get; set; } = 0.15;
        public double EdgeLineHalfWidth{ get; set; } = 0.08;
        public double EdgeOffset       { get; set; } = 3.5;

        public double AsphaltNoiseAmp  { get; set; } = 0.015;
        public double SidewalkNoiseAmp { get; set; } = 0.006;
        public double WallWaveAmp      { get; set; } = 0.08;
        public (double Min, double Max) IntensityRange { get; set; } = (0.35, 0.95);

        public (byte R, byte G, byte B) ColAsphalt        { get; set; } = (50, 50, 55);
        public (byte R, byte G, byte B) ColMarkingWhite   { get; set; } = (230,230,230);
        public (byte R, byte G, byte B) ColMarkingYellow  { get; set; } = (210,180, 40);
        public (byte R, byte G, byte B) ColSidewalk       { get; set; } = (165,165,165);
        public (byte R, byte G, byte B) ColCurb           { get; set; } = (125,125,125);
        public (byte R, byte G, byte B) ColBuilding       { get; set; } = (190,185,175);
        public (byte R, byte G, byte B) ColPole           { get; set; } = (220,220,220);
        public (byte R, byte G, byte B) ColTreeTrunk      { get; set; } = (110,70,40);
        public (byte R, byte G, byte B) ColTreeLeaf       { get; set; } = (45,150,60);

        // 显式声明元素元组类型，避免 (int,int,int) → (byte,byte,byte) 推断错误
        public (byte R, byte G, byte B)[] CarColors { get; set; } = new (byte R, byte G, byte B)[]
        {
            (200, 30, 30),
            (230, 160, 0),
            (30, 120, 220),
            (240, 240, 240),
            (40, 40, 40)
        };
    }
}
