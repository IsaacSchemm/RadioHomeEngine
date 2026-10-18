namespace RadioHomeEngine

open System.Threading.Tasks

type ISegmentSourceFactory =
    abstract member CreateAsync: unit -> Task<ISegmentSource>
