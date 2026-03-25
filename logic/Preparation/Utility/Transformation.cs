namespace Preparation.Utility
{
    public static class Transformation
    {
        public static global::Protobuf.CharacterType CharacterTypeToProto(CharacterType type)
            => (global::Protobuf.CharacterType)(int)type;

        public static CharacterType CharacterTypeFromProto(global::Protobuf.CharacterType type)
            => (CharacterType)(int)type;

        public static global::Protobuf.CharacterState CharacterStateToProto(CharacterState state)
            => (global::Protobuf.CharacterState)(int)state;

        public static global::Protobuf.ResourceType ResourceTypeToProto(ResourceType type)
            => (global::Protobuf.ResourceType)(int)type;

        public static global::Protobuf.ResourceState ResourceStateToProto(ResourceState state)
            => (global::Protobuf.ResourceState)(int)state;

        public static global::Protobuf.GoodsType GoodsTypeToProto(GoodsType type)
            => (global::Protobuf.GoodsType)(int)type;

        public static GoodsType GoodsTypeFromProto(global::Protobuf.GoodsType type)
            => (GoodsType)(int)type;

        public static global::Protobuf.MarketType MarketTypeToProto(MarketType type)
            => (global::Protobuf.MarketType)(int)type;

        public static global::Protobuf.PlaceType PlaceTypeToProto(PlaceType type)
            => (global::Protobuf.PlaceType)(int)type;
    }
}
