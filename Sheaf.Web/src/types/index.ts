export interface UserInfo {
  userId: string;
  userName: string;
  email: string;
  isAuthenticated: boolean;
}

export type LoginRequest = {
  email: string;
  password: string;
  rememberMe?: boolean;
}

export type RegisterRequest = {
  email: string;
  password: string;
  confirmPassword: string;
}

export interface ApiResponse<T = void> {
  success: boolean;
  data?: T;
  errors?: string[];
}
