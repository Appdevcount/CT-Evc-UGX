export const RequestReducer = (state: any, action: any) => {
    switch (action.type) {
      case "SET_REQUEST":
        return {
          ...state,
          request: action.payload,
        };
      case "SET_LOOKUP_REQUEST":
        return {
          ...state,
          lookupRequest: action.payload,
        };
      case "SET_GET_NEXT_REQUEST_KEY":
        return {
          ...state,
          getNextRequestId: action.payload,
        };
      case "SET_REFRESH_REQUEST_KEY":
        return {
          ...state,
          refreshRequestKey: action.payload,
        };
      case "SET_REFRESH":
        return {
          ...state,
          refresh: action.payload,
        };
      case "SET_ERROR":
        return {
          ...state,
          error: action.payload,
        };
      case "SET_BAD_TOKEN":
        return {
          ...state,
          badToken: action.payload,
        };
      case "SET_ACCESS_TOKEN":
        if (action.payload === state.accessToken) return state;
        return {
          ...state,
          accessToken: action.payload,
        };
      case "SET_IDLE_TIMEOUT":
        return {
          ...state,
          idleTimeout: action.payload,
        };
      case "SET_ORIGIN_SYSTEM":
        return {
          ...state,
          originSystem: action.payload,
        };
      default:
        return state;
    }
  };
  